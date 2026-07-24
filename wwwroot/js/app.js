import * as api from "./api.js";
import * as router from "./router.js";
import { FileBrowserDialog } from "./fileBrowserDialog.js";

const tokenInput = document.getElementById("token-input");
const btnLogin = document.getElementById("btn-login");
const btnLogout = document.getElementById("btn-logout");
const btnOpen = document.getElementById("btn-open-browser");
const authStatus = document.getElementById("auth-status");
const dialogEl = document.getElementById("browser-dialog");

const browser = new FileBrowserDialog(dialogEl);
// poll because stopping or restarting the local server invalidates the per-run token
const SESSION_CHECK_INTERVAL_MS = 3000;
let sessionCheckInFlight = false;

function setAuthStatus(message, kind) {
    authStatus.textContent = message;
    authStatus.className = `status ${kind || ""}`;
}

function syncAuthUi() {
    const token = api.getToken();
    const role = api.getRole();
    const signedIn = Boolean(token && role);

    btnLogout.disabled = !signedIn;
    btnOpen.disabled = !signedIn;
    tokenInput.disabled = signedIn;

    if (signedIn) {
        setAuthStatus(`Signed in as ${role}.`, "ok");
    }
}

function expireBrowserSession(message) {
    api.clearSession();
    tokenInput.value = "";

    if (dialogEl.open) {
        dialogEl.close();
    }

    syncAuthUi();
    setAuthStatus(message, "error");
}

async function validateServerSession() {
    // avoid overlapping heartbeats when an interval and focus event fire together
    if (!api.getToken() || sessionCheckInFlight) {
        return Boolean(api.getToken());
    }

    sessionCheckInFlight = true;
    try {
        const current = await api.currentUser();

        api.setSession(api.getToken(), current.role);
        syncAuthUi();
        return true;
    } catch (err) {
        const rejected = err.status === 401 || err.status === 403;
        expireBrowserSession(
            rejected
                ? "API key is no longer valid. Sign in with a token from the current server run."
                : "Server session ended or is unavailable. You have been signed out."
        );
        return false;
    } finally {
        sessionCheckInFlight = false;
    }
}

async function handleLogin() {
    const token = tokenInput.value.trim();
    if (!token) {
        setAuthStatus("Enter an API token.", "error");
        return;
    }
    try {
        const result = await api.login(token);
        tokenInput.value = "";
        setAuthStatus(result.message, "ok");
        syncAuthUi();
        await validateServerSession();

        const route = router.readRoute();
        if (route.open) {
            await browser.openFromRoute(route);
        }
    } catch (err) {
        setAuthStatus(err.message, "error");
    }
}

function handleLogout() {
    api.clearSession();
    browser.close();
    setAuthStatus("Signed out.", "ok");
    syncAuthUi();
}

async function handleOpenBrowser() {
    if (!api.getToken()) {
        setAuthStatus("Sign in first.", "error");
        return;
    }
    if (!await validateServerSession()) {
        return;
    }

    const route = router.readRoute();
    await browser.openFromRoute({
        path: route.path || "",
        q: route.q || "",
        open: true
    });
    router.writeRoute({ path: route.path || "", q: route.q || "", open: true });
}

async function handleRouteChange() {
    const route = router.readRoute();
    if (!route.open) {
        if (dialogEl.open) {
            dialogEl.close();
        }
        return;
    }
    if (!api.getToken()) {
        setAuthStatus("Deep link detected — sign in to open the browser.", "error");
        return;
    }
    if (await validateServerSession()) {
        await browser.openFromRoute(route);
    }
}

btnLogin.addEventListener("click", handleLogin);
btnLogout.addEventListener("click", handleLogout);
btnOpen.addEventListener("click", handleOpenBrowser);

dialogEl.addEventListener("close", () => {
    const route = router.readRoute();
    if (route.open) {
        router.writeRoute({ open: false }, { replace: true });
    }
});

router.onRouteChange(() => {
    handleRouteChange().catch((err) => setAuthStatus(err.message, "error"));
});

async function initialize() {
    syncAuthUi();

    if (api.getToken()) {
        await validateServerSession();
    }

    await handleRouteChange();

    window.setInterval(() => {
        if (api.getToken()) {
            validateServerSession().catch(() => {
                // already update and clear the UI state inside validateServerSession
            });
        }
    }, SESSION_CHECK_INTERVAL_MS);

    window.addEventListener("focus", () => {
        if (api.getToken()) {
            validateServerSession();
        }
    });
    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "visible" && api.getToken()) {
            validateServerSession();
        }
    });
}

initialize().catch((err) => {
    expireBrowserSession(err.message || "Unable to initialize session.");
});
