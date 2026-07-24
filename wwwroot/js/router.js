// use hash routing to keep client state deep-linkable without a server catch-all
// deliberately keep credentials out of this state
export function readRoute() {
    const raw = window.location.hash.startsWith("#")
        ? window.location.hash.slice(1)
        : window.location.hash;

    if (!raw.startsWith("/browse")) {
        return { path: "", q: "", open: false };
    }

    const queryIndex = raw.indexOf("?");
    const search = queryIndex >= 0 ? raw.slice(queryIndex + 1) : "";
    const params = new URLSearchParams(search);

    return {
        path: params.get("path") || "",
        q: params.get("q") || "",
        open: true
    };
}

export function writeRoute({ path = "", q = "", open = true }, { replace = false } = {}) {
    if (!open) {
        const target = "#";
        if (replace) {
            window.location.replace(target);
        } else if (window.location.hash !== target && window.location.hash !== "") {
            window.location.hash = "";
        }
        return;
    }

    const params = new URLSearchParams();
    if (path) params.set("path", path);
    if (q) params.set("q", q);
    const qs = params.toString();
    const target = qs ? `#/browse?${qs}` : "#/browse";

    if (replace) {
        // notify the app because replaceState does not emit hashchange
        const url = `${window.location.pathname}${window.location.search}${target}`;
        window.history.replaceState(null, "", url);
        window.dispatchEvent(new HashChangeEvent("hashchange"));
    } else if (window.location.hash !== target) {
        window.location.hash = target.slice(1); // location.hash setter adds #
    }
}

export function onRouteChange(handler) {
    window.addEventListener("hashchange", handler);
}
