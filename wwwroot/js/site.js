// Tailwind-only modal helper (no Bootstrap)
(function () {
  function showModal(id) {
    var el = document.getElementById(id);
    if (el) {
      el.classList.remove('hidden');
      el.setAttribute('aria-hidden', 'false');
      document.body.classList.add('overflow-hidden');
    }
  }
  function hideModal(idOrEl) {
    var el = typeof idOrEl === 'string' ? document.getElementById(idOrEl) : idOrEl;
    if (el) {
      el.classList.add('hidden');
      el.setAttribute('aria-hidden', 'true');
      document.body.classList.remove('overflow-hidden');
    }
  }
  function initModals() {
    document.querySelectorAll('[data-modal-show]').forEach(function (btn) {
      btn.addEventListener('click', function () {
        var id = btn.getAttribute('data-modal-show');
        if (id) showModal(id);
      });
    });
    document.querySelectorAll('[data-modal-close]').forEach(function (btn) {
      btn.addEventListener('click', function () {
        var id = btn.getAttribute('data-modal-close');
        if (id) hideModal(id);
        else {
          var wrapper = btn.closest('.tw-modal');
          if (wrapper) hideModal(wrapper);
        }
      });
    });
    document.querySelectorAll('.tw-modal').forEach(function (modal) {
      var overlay = modal.querySelector('[data-modal-close][class*="inset-0"]');
      if (overlay) {
        overlay.addEventListener('click', function () {
          hideModal(modal);
        });
      }
    });
  }
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initModals);
  } else {
    initModals();
  }
  window.showModal = showModal;
  window.hideModal = hideModal;
})();
