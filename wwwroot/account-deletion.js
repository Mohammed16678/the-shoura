document.querySelector('#delete-form').addEventListener('submit', async event => {
  event.preventDefault();
  const status = document.querySelector('#status');
  if (location.protocol !== 'https:' && location.hostname !== 'localhost' && location.hostname !== '127.0.0.1') {
    status.textContent = 'الاتصال الآمن مطلوب لإكمال الطلب.';
    return;
  }
  if (!confirm('هل تريد حذف الحساب نهائيًا؟')) return;
  const form = new FormData(event.currentTarget);
  try {
    let response = await fetch('/api/auth/login', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: form.get('email'), password: form.get('password') })
    });
    if (!response.ok) throw Error('تعذر التحقق من بيانات الدخول.');
    const auth = await response.json();
    response = await fetch('/api/account/me', { method: 'DELETE', headers: { Authorization: `Bearer ${auth.accessToken}` } });
    if (!response.ok) throw Error('تعذر حذف الحساب. حاول مرة أخرى.');
    event.currentTarget.reset();
    status.textContent = 'تم حذف الحساب والبيانات المرتبطة به.';
  } catch (error) {
    status.textContent = error.message;
  }
});
