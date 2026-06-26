window.acuratexCanRx = window.acuratexCanRx || {};
window.acuratexCanRx.copyText = async function (text) {
  const value = String(text ?? '');
  if (!value) {
    return;
  }

  try {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      await navigator.clipboard.writeText(value);
      return;
    }
  } catch (error) {
    console.warn('acuratexCanRx.copyText clipboard failed', error);
  }

  const textarea = document.createElement('textarea');
  textarea.value = value;
  textarea.setAttribute('readonly', '');
  textarea.style.position = 'fixed';
  textarea.style.opacity = '0';
  textarea.style.left = '-9999px';
  document.body.appendChild(textarea);
  textarea.select();
  document.execCommand('copy');
  document.body.removeChild(textarea);
};
