// يُحمَّل في رأس الصفحة قبل الرسم حتى لا يظهر وميض بين الثيمات.
(function () {
  var theme = 'dark', size = 'md';
  try {
    var s = JSON.parse(localStorage.getItem('shoura_settings') || '{}');
    if (s.theme) theme = s.theme;
    if (s.size) size = s.size;
  } catch (e) {}
  if (theme === 'auto') {
    theme = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
  var root = document.documentElement;
  root.setAttribute('data-theme', theme);
  root.setAttribute('data-size', size);
})();
