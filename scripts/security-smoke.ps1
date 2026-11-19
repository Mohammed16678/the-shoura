$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$project = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\backend\Shoura.API\Shoura.API.csproj'))
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('shoura-security-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $temp
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$baseUrl = "http://127.0.0.1:$port"
$stdout = Join-Path $temp 'server.out.log'; $stderr = Join-Path $temp 'server.err.log'
$oldEnvironment = @{}
foreach ($name in @('ASPNETCORE_ENVIRONMENT', 'SHOURA_DATABASE_PROVIDER', 'SHOURA_AUTH_SIGNING_KEY', 'SHOURA_MEDIA_PATH', 'ConnectionStrings__DefaultConnection', 'Moderation__AdminUserIds__0')) { $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$apiProcess = $null
$checks = 0
function Assert-Check([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw "FAILED: $Description" }
    $script:checks++
    Write-Host "PASS: $Description"
}
function Send-Request([string]$Method, [string]$Path, [string]$Token = $null, [object]$Body = $null, [string]$ContentType = 'application/json') {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$script:baseUrl$Path")
    if ($Token) { $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Token) }
    if ($null -ne $Body) {
        if ($Body -is [byte[]]) { $request.Content = [System.Net.Http.ByteArrayContent]::new($Body); $request.Content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($ContentType) }
        else { $request.Content = [System.Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 8 -Compress), [Text.Encoding]::UTF8, $ContentType) }
    }
    return $script:http.SendAsync($request).GetAwaiter().GetResult()
}
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:SHOURA_DATABASE_PROVIDER = 'Sqlite'
    $env:SHOURA_AUTH_SIGNING_KEY = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')
    $env:SHOURA_MEDIA_PATH = Join-Path $temp 'uploads'
    $env:ConnectionStrings__DefaultConnection = 'Data Source=' + (Join-Path $temp 'test.db')
    $env:Moderation__AdminUserIds__0 = '1'
    $apiProcess = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--no-build', '--project', ('"' + $project + '"'), '--no-launch-profile', '--', '--urls', $baseUrl) -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $handler = [System.Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
    $http = [System.Net.Http.HttpClient]::new($handler); $http.Timeout = [TimeSpan]::FromSeconds(10)
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        if ($apiProcess.HasExited) { $details = Get-Content -Raw $stderr -ErrorAction SilentlyContinue; throw "API exited during startup: $details" }
        try { $response = $http.GetAsync("$baseUrl/health").GetAwaiter().GetResult(); if ($response.IsSuccessStatusCode) { $ready = $true; break } } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { $details = (Get-Content -Raw $stderr -ErrorAction SilentlyContinue) + (Get-Content -Raw $stdout -ErrorAction SilentlyContinue); throw "API did not become ready: $details" }
    Assert-Check $ready 'API starts and health endpoint responds'
    Assert-Check ($response.Headers.Contains('Content-Security-Policy')) 'security headers are present'
    Assert-Check (($http.GetAsync("$baseUrl/privacy-policy.html").GetAwaiter().GetResult()).IsSuccessStatusCode) 'privacy policy is published'
    Assert-Check (($http.GetAsync("$baseUrl/terms.html").GetAwaiter().GetResult()).IsSuccessStatusCode) 'terms are published'
    $response = Send-Request GET '/api/users'
    Assert-Check ($response.StatusCode.value__ -eq 401) 'sensitive user route rejects anonymous requests'
    $response = Send-Request POST '/api/posts' $null @{ content = 'anonymous'; mediaType = 'text' }
    Assert-Check ($response.StatusCode.value__ -eq 401) 'post creation rejects anonymous requests'

    $account = @{ name = 'Security Check'; handle = 'security_check'; email = 'security-check@example.test'; password = 'TestPassword!234'; bio = 'SQL input: quote and semicolon'; acceptTerms = $true }
    $response = Send-Request POST '/api/auth/register' $null $account
    Assert-Check ($response.StatusCode.value__ -eq 200) 'valid account can register'
    Assert-Check ($response.Headers.CacheControl.ToString() -eq 'no-store') 'authentication responses are not cached'
    $auth = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    $token = $auth.accessToken
    $response = Send-Request GET '/api/users' $token
    Assert-Check ($response.IsSuccessStatusCode) 'valid session can access authorized route'
    $recipient = @{ name = 'Private Media Recipient'; handle = 'private_recipient'; email = 'recipient@example.test'; password = 'TestPassword!234'; acceptTerms = $true }
    $response = Send-Request POST '/api/auth/register' $null $recipient
    $recipientAuth = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    $outsider = @{ name = 'Private Media Outsider'; handle = 'private_outsider'; email = 'outsider@example.test'; password = 'TestPassword!234'; acceptTerms = $true }
    $response = Send-Request POST '/api/auth/register' $null $outsider
    $outsiderAuth = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json

    $response = Send-Request GET '/api/users' ($token + 'tampered')
    Assert-Check ($response.StatusCode.value__ -eq 401) 'tampered session token is rejected'
    $response = Send-Request POST '/api/auth/login' $null @{ email = "nobody' OR '1'='1@example.test"; password = 'TestPassword!234' }
    Assert-Check ($response.StatusCode.value__ -in @(400, 401)) 'SQL injection in login cannot authenticate'
    $response = Send-Request POST '/api/messages' $token @{ receiverId = [int]$recipientAuth.user.id; content = "'; DROP TABLE Users; -- <script>alert(1)</script>"; mediaType = 'text' }
    Assert-Check ($response.IsSuccessStatusCode) 'SQL payload in a private message is stored as text'
    $response = Send-Request GET "/api/messages/$($recipientAuth.user.id)" $token
    $messageJson = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Assert-Check ($messageJson.Contains('DROP TABLE Users')) 'SQL payload remains data in the message response'
    $response = Send-Request GET '/api/users' $token
    Assert-Check ($response.IsSuccessStatusCode) 'database remains available after injection attempt'
    $response = Send-Request POST '/api/posts' $token @{ content = 'posting disabled'; mediaType = 'text' }
    Assert-Check ($response.StatusCode.value__ -eq 410) 'new posts are disabled'
    $response = Send-Request GET '/api/posts'
    $feed = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Assert-Check ($feed.Contains('"items":[]')) 'post feed stays empty'

    $login = @{ email = $account.email; password = $account.password }
    $login.acceptTerms = $true
    $response = Send-Request POST '/api/auth/login' $null $login
    $auth2 = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    Assert-Check ($auth2.accessToken -ne $token) 'new login receives a rotated session token'

    $response = Send-Request POST '/api/media/upload' $null
    Assert-Check ($response.StatusCode.value__ -eq 401) 'file upload rejects anonymous requests'
    $multipart = [System.Net.Http.MultipartFormDataContent]::new()
    $fake = [System.Net.Http.ByteArrayContent]::new([Text.Encoding]::UTF8.GetBytes('<html>not a video</html>'))
    $fake.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('video/mp4')
    $multipart.Add($fake, 'file', 'fake.mp4')
    $uploadRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$baseUrl/api/media/upload")
    $uploadRequest.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
    $uploadRequest.Content = $multipart
    $response = $http.SendAsync($uploadRequest).GetAwaiter().GetResult()
    Assert-Check ($response.StatusCode.value__ -eq 400) 'spoofed media extension and MIME type are rejected'

    $multipart = [System.Net.Http.MultipartFormDataContent]::new()
    $audio = [System.Net.Http.ByteArrayContent]::new([byte[]](0x4F, 0x67, 0x67, 0x53))
    $audio.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('audio/ogg')
    $multipart.Add($audio, 'file', 'voice.ogg')
    $uploadRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$baseUrl/api/media/upload")
    $uploadRequest.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
    $uploadRequest.Content = $multipart
    $response = $http.SendAsync($uploadRequest).GetAwaiter().GetResult()
    Assert-Check ($response.IsSuccessStatusCode) 'valid media can be uploaded'
    $media = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    $response = Send-Request POST '/api/messages' $token @{ receiverId = [int]$recipientAuth.user.id; content = ''; mediaUrl = $media.url; mediaType = 'audio' }
    $messageStatus = $response.StatusCode.value__
    $messageBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Assert-Check ($response.IsSuccessStatusCode) "audio can be attached to a private conversation (HTTP $messageStatus): $messageBody"
    $privateMessage = $messageBody | ConvertFrom-Json
    $response = Send-Request GET $media.url
    Assert-Check ($response.StatusCode.value__ -eq 404) 'private message media is hidden from anonymous visitors'
    $response = Send-Request GET $media.url $outsiderAuth.accessToken
    Assert-Check ($response.StatusCode.value__ -eq 404) 'private message media is hidden from nonparticipants'
    $response = Send-Request GET $media.url $recipientAuth.accessToken
    Assert-Check ($response.IsSuccessStatusCode) 'conversation participants can access private media'
    $response = Send-Request POST '/api/posts' $token @{ content = 'public voice post'; mediaUrl = $media.url; mediaType = 'audio' }
    Assert-Check ($response.StatusCode.value__ -eq 410) 'users cannot republish media as a post'
    $response = Send-Request GET $media.url
    Assert-Check ($response.StatusCode.value__ -eq 404) 'unpublished private media stays inaccessible to anonymous visitors'

    $response = Send-Request POST '/api/moderation/reports' $token @{ messageId = $privateMessage.id; category = 'child_safety'; details = 'security smoke report' }
    Assert-Check ($response.StatusCode.value__ -eq 202) 'in-app child-safety reports are accepted'
    $response = Send-Request GET '/api/moderation/reports' $outsiderAuth.accessToken
    Assert-Check ($response.StatusCode.value__ -eq 403) 'moderation report review rejects non-admin users'
    $response = Send-Request GET '/api/moderation/reports' $token
    Assert-Check ($response.IsSuccessStatusCode) 'configured safety administrator can review reports'
    $openReports = @($response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json)
    Assert-Check ($openReports.Count -gt 0) 'reported child-safety item appears in the review queue'
    $reportId = $openReports[0].id
    $response = Send-Request DELETE "/api/moderation/reports/$reportId/message" $token
    Assert-Check ($response.IsSuccessStatusCode) 'authorized moderator can remove a reported message'

    $response = Send-Request POST "/api/users/$($recipientAuth.user.id)/block" $token
    Assert-Check ($response.IsSuccessStatusCode) 'users can block accounts in the app'
    $response = Send-Request GET "/api/messages/$($recipientAuth.user.id)" $token
    Assert-Check ($response.StatusCode.value__ -eq 404) 'blocking prevents access to that conversation'
    $response = Send-Request DELETE "/api/users/$($recipientAuth.user.id)/block" $token
    Assert-Check ($response.IsSuccessStatusCode) 'users can undo a block'

    $limited = $false
    for ($i = 0; $i -lt 12; $i++) {
        $response = Send-Request POST '/api/auth/login' $null @{ email = "missing$i@example.test"; password = 'WrongPassword!234' }
        if ($response.StatusCode.value__ -eq 429) { $limited = $true; break }
    }
    Assert-Check $limited 'authentication endpoint is rate limited'

    $response = Send-Request POST '/api/account/logout' $token
    Assert-Check ($response.StatusCode.value__ -eq 204) 'authenticated logout revokes the active session'
    $response = Send-Request GET '/api/users' $token
    Assert-Check ($response.StatusCode.value__ -eq 401) 'revoked session token cannot be reused'
    Write-Host "Security smoke checks passed: $checks"
} finally {
    if ($http) { $http.Dispose() }
    if ($apiProcess -and -not $apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id -Force }
    foreach ($name in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name], 'Process') }
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
