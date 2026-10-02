// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// ==========================================================
// Show/hide button on every password field.
// Add data-no-pw-toggle to an input that already has its own toggle.
// ==========================================================
(function () {
    var EYE = '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>';
    var EYE_OFF = '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/></svg>';

    function addToggle(input) {
        if (input.dataset.pwToggleAdded || input.hasAttribute('data-no-pw-toggle')) return;
        input.dataset.pwToggleAdded = '1';

        var parent = input.parentElement;
        if (window.getComputedStyle(parent).position === 'static') parent.style.position = 'relative';

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'pw-toggle-btn';
        btn.title = 'Show password';
        btn.setAttribute('aria-label', 'Show password');
        btn.setAttribute('aria-pressed', 'false');
        btn.innerHTML = EYE;
        btn.style.cssText = 'position:absolute;z-index:5;display:flex;align-items:center;justify-content:center;width:36px;border:0;background:transparent;padding:0;color:#6c757d;cursor:pointer;';

        function place() {
            btn.style.top = input.offsetTop + 'px';
            btn.style.height = input.offsetHeight + 'px';
            btn.style.left = (input.offsetLeft + input.offsetWidth - 40) + 'px';
        }

        input.style.paddingRight = '2.5rem';
        input.after(btn);
        place();
        window.addEventListener('resize', place);
        // layout can shift after fonts/validation messages load
        setTimeout(place, 300);
        input.addEventListener('focus', place);
        if (window.ResizeObserver) new ResizeObserver(place).observe(input); // fields inside modals get sized only when shown

        // keep focus in the input so the caret/keyboard do not jump
        btn.addEventListener('mousedown', function (e) { e.preventDefault(); });
        btn.addEventListener('click', function () {
            var show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            btn.innerHTML = show ? EYE_OFF : EYE;
            btn.title = show ? 'Hide password' : 'Show password';
            btn.setAttribute('aria-label', btn.title);
            btn.setAttribute('aria-pressed', show ? 'true' : 'false');
        });

        // never leave the password visible when the form is submitted
        if (input.form) {
            input.form.addEventListener('submit', function () {
                if (input.type === 'text' && btn.getAttribute('aria-pressed') === 'true') input.type = 'password';
            });
        }
    }

    function init() {
        document.querySelectorAll('input[type="password"]').forEach(addToggle);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
