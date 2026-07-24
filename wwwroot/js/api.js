

const TOKEN_KEY = "fb_api_token";
const ROLE_KEY = "fb_api_role";

// use sessionStorage to keep the credential out of URLs and clear it with the tab.
export function getToken() {
    return sessionStorage.getItem(TOKEN_KEY) || "";
}

export function getRole() {
    return sessionStorage.getItem(ROLE_KEY) || "";
}

export function setSession(token, role) {
    sessionStorage.setItem(TOKEN_KEY, token);
    sessionStorage.setItem(ROLE_KEY, role);
}

export function clearSession() {
    sessionStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(ROLE_KEY);
}

export async function apiRequest(path, options = {}) {
    const headers = new Headers(options.headers || {});
    const token = getToken();

    if (token) {
        headers.set("Authorization", `Bearer ${token}`);
    }

    if (options.body && !(options.body instanceof FormData) && !headers.has("Content-Type")) {
        headers.set("Content-Type", "application/json");
    }

    const response = await fetch(path, {
        ...options,
        headers,
        credentials: "same-origin"
    });

    const text = await response.text();
    let data = null;
    if (text) {
        try {
            data = JSON.parse(text);
        } catch {
            data = { error: text };
        }
    }

    if (!response.ok) {
        const message = (data && (data.error || data.detail)) || `Request failed (${response.status})`;
        const error = new Error(message);
        error.status = response.status;
        throw error;
    }

    return data;
}

export async function login(apiToken) {
    const response = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "same-origin",
        body: JSON.stringify({ apiToken })
    });
    const data = await response.json();
    if (!response.ok) {
        throw new Error(data.error || "Login failed");
    }
    setSession(apiToken, data.role);
    return data;
}

export function currentUser() {
    return apiRequest("/api/auth/me");
}

export function browse(path) {
    const qs = new URLSearchParams();
    if (path) qs.set("path", path);
    return apiRequest(`/api/files/browse?${qs.toString()}`);
}

export function search(path, q) {
    const qs = new URLSearchParams();
    if (path) qs.set("path", path);
    qs.set("q", q);
    return apiRequest(`/api/files/search?${qs.toString()}`);
}

export async function download(path) {
    const qs = new URLSearchParams({ path });
    const response = await fetch(`/api/files/download?${qs.toString()}`, {
        headers: { Authorization: `Bearer ${getToken()}` },
        credentials: "same-origin"
    });
    if (!response.ok) {
        let message = `Download failed (${response.status})`;
        try {
            const data = await response.json();
            message = data.error || message;
        } catch {  }
        throw new Error(message);
    }

    const blob = await response.blob();
    const disposition = response.headers.get("Content-Disposition") || "";
    const match = /filename\*?=(?:UTF-8''|")?([^\";]+)/i.exec(disposition);
    const fileName = match ? decodeURIComponent(match[1].replace(/"/g, "")) : "download.bin";

    const url = URL.createObjectURL(blob);
    // fetch the authenticated response first because an anchor cannot attach the
    // Bearer header, then hand its object URL to the browser
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
}

export async function upload(path, file) {
    const qs = new URLSearchParams();
    if (path) qs.set("path", path);
    const form = new FormData();
    form.append("file", file, file.name);
    return apiRequest(`/api/files/upload?${qs.toString()}`, { method: "POST", body: form });
}

export function deletePath(path) {
    return apiRequest("/api/files/delete", {
        method: "POST",
        body: JSON.stringify({ path })
    });
}

export function copyPath(sourcePath, destinationPath) {
    return apiRequest("/api/files/copy", {
        method: "POST",
        body: JSON.stringify({ sourcePath, destinationPath })
    });
}

export function movePath(sourcePath, destinationPath) {
    return apiRequest("/api/files/move", {
        method: "POST",
        body: JSON.stringify({ sourcePath, destinationPath })
    });
}
