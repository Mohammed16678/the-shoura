(() => {
  const key = 'shoura-cookie-choice-v1';
  let saved = null;
  try { saved = localStorage.getItem(key); } catch {}
  if (saved) return;
  const banner = document.getElementById('cookie-consent');
  if (!banner) return;
  const message = document.createElement('span');
  message.textContent = 'يستخدم هذا الموقع التخزين المحلي لحفظ اختيارك فقط. لا نستخدم كوكيز إعلانية أو تحليلات حاليًا.';
  banner.append(message);
  for (const [choice, label] of [['essential', 'الأساسيات فقط'], ['all', 'موافق']]) {
    const button = document.createElement('button');
    button.type = 'button';
    button.dataset.choice = choice;
    button.textContent = label;
    banner.append(button);
  }
  banner.style.display = 'block';
  banner.addEventListener('click', event => {
    const choice = event.target.closest('button')?.dataset.choice;
    if (!choice) return;
    try { localStorage.setItem(key, choice); } catch {}
    banner.remove();
  });
})();
