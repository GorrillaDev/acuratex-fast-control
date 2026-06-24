// [ACURATEX] Hotkeys globales para capturar Escape y reenviarlo a Blazor cuando la UI esta activa.
// [FLUJO] Tecla Escape -> JavaScript -> DotNetObjectReference -> metodo [JSInvokable].
// [EQUIV MCU] Es parecido a una interrupcion de teclado que dispara un callback.
(function () {
    const listeners = new Map();
    let nextId = 1;

    function registerEscape(dotNetRef, methodName) {
        if (!dotNetRef || !methodName) {
            return 0;
        }

        const listener = function (event) {
            if (event.key !== "Escape") {
                return;
            }

            if (event.repeat) {
                return;
            }

            try {
                dotNetRef.invokeMethodAsync(methodName);
            } catch {
                // Ignored: host may be disposing.
            }
        };

        const id = nextId++;
        listeners.set(id, listener);
        window.addEventListener("keydown", listener, true);
        return id;
    }

    function unregister(id) {
        if (!listeners.has(id)) {
            return;
        }

        const listener = listeners.get(id);
        listeners.delete(id);
        window.removeEventListener("keydown", listener, true);
    }

    window.acuratexHotkeys = window.acuratexHotkeys || {
        registerEscape,
        unregister
    };
})();
