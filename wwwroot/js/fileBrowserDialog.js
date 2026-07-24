

import * as api from "./api.js";
import * as router from "./router.js";

// keep dialog state and rendering here while api.js and router.js handle transport
// and URL concerns
function formatBytes(bytes) {
    if (bytes === 0) return "0 B";
    if (bytes == null) return "—";
    const units = ["B", "KB", "MB", "GB"];
    let value = bytes;
    let i = 0;
    while (value >= 1024 && i < units.length - 1) {
        value /= 1024;
        i++;
    }
    return `${value.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

export class FileBrowserDialog {
    
    constructor(dialog, hooks = {}) {
        this.dialog = dialog;
        this.onStatus = hooks.onStatus || (() => {});

        this.pathInput = dialog.querySelector("#path-input");
        this.searchInput = dialog.querySelector("#search-input");
        this.stats = dialog.querySelector("#stats");
        this.browserStatus = dialog.querySelector("#browser-status");
        this.tbody = dialog.querySelector("#entries-body");
        this.fileInput = dialog.querySelector("#file-input");

        this.currentPath = "";
        this.currentQuery = "";
        this.selectedPath = null;
        this.selectedIsDirectory = false;

        this.#bindEvents();
    }

    #bindEvents() {
        this.dialog.querySelector("#btn-home").addEventListener("click", () => this.navigate("", ""));
        this.dialog.querySelector("#btn-up").addEventListener("click", () => this.goUp());
        this.dialog.querySelector("#btn-go").addEventListener("click", () => {
            this.navigate(this.pathInput.value.trim(), this.currentQuery);
        });
        this.dialog.querySelector("#btn-search").addEventListener("click", () => {
            this.navigate(this.currentPath, this.searchInput.value.trim());
        });
        this.dialog.querySelector("#btn-clear-search").addEventListener("click", () => {
            this.searchInput.value = "";
            this.navigate(this.currentPath, "");
        });
        this.dialog.querySelector("#btn-download").addEventListener("click", () => this.downloadSelected());
        this.dialog.querySelector("#btn-delete").addEventListener("click", () => this.deleteSelected());
        this.dialog.querySelector("#btn-copy").addEventListener("click", () => this.copyOrMove("copy"));
        this.dialog.querySelector("#btn-move").addEventListener("click", () => this.copyOrMove("move"));

        this.fileInput.addEventListener("change", async () => {
            const file = this.fileInput.files && this.fileInput.files[0];
            this.fileInput.value = "";
            if (!file) return;
            try {
                this.#requireOperator();
                await api.upload(this.currentPath, file);
                this.#setBrowserStatus("Upload succeeded.", "ok");
                await this.refresh();
            } catch (err) {
                this.#setBrowserStatus(err.message, "error");
            }
        });

        this.pathInput.addEventListener("keydown", (e) => {
            if (e.key === "Enter") {
                e.preventDefault();
                this.navigate(this.pathInput.value.trim(), this.currentQuery);
            }
        });
        this.searchInput.addEventListener("keydown", (e) => {
            if (e.key === "Enter") {
                e.preventDefault();
                this.navigate(this.currentPath, this.searchInput.value.trim());
            }
        });
    }

    
    async openFromRoute(route) {
        if (!this.dialog.open) {
            this.dialog.showModal();
        }
        await this.navigate(route.path || "", route.q || "", { skipRouteWrite: true });
    }

    close() {
        if (this.dialog.open) {
            this.dialog.close();
        }
        router.writeRoute({ open: false });
    }

    async navigate(path, q, { skipRouteWrite = false } = {}) {
        this.currentPath = path || "";
        this.currentQuery = q || "";
        this.pathInput.value = this.currentPath;
        this.searchInput.value = this.currentQuery;
        this.selectedPath = null;

        if (!skipRouteWrite) {
            router.writeRoute({ path: this.currentPath, q: this.currentQuery, open: true });
        }

        await this.refresh();
    }

    async refresh() {
        try {
            const data = this.currentQuery
                ? await api.search(this.currentPath, this.currentQuery)
                : await api.browse(this.currentPath);

            this.currentPath = data.currentPath || "";
            this.pathInput.value = this.currentPath;
            this.#render(data);
            this.#setBrowserStatus(this.currentQuery ? `Search results for "${this.currentQuery}".` : "Browse OK.", "ok");
            this.#updateWriteButtons();
        } catch (err) {
            this.#setBrowserStatus(err.message, "error");
        }
    }

    goUp() {
        if (!this.currentPath) {
            this.navigate("", this.currentQuery);
            return;
        }
        const parts = this.currentPath.split("/").filter(Boolean);
        parts.pop();
        this.navigate(parts.join("/"), "");
    }

    #render(data) {
        this.tbody.replaceChildren();

        this.stats.textContent =
            `Folders: ${data.folderCount} · Files: ${data.fileCount} · ` +
            `Total file size (current view): ${formatBytes(data.totalFileSizeBytes)}` +
            (data.query ? ` · Query: ${data.query}` : "");

        for (const entry of data.entries) {
            const tr = document.createElement("tr");

            const tdSelect = document.createElement("td");
            const radio = document.createElement("input");
            radio.type = "radio";
            radio.name = "selected-entry";
            radio.addEventListener("change", () => {
                this.selectedPath = entry.relativePath;
                this.selectedIsDirectory = entry.isDirectory;
                this.tbody.querySelectorAll("tr").forEach((row) => row.classList.remove("selected"));
                tr.classList.add("selected");
                this.#updateWriteButtons();
            });
            tdSelect.appendChild(radio);

            const tdName = document.createElement("td");
            if (entry.isDirectory) {
                const btn = document.createElement("button");
                btn.type = "button";
                btn.className = "linkish";
                // I treat filenames as persistent untrusted data and never insert them as HTML.
                btn.textContent = entry.name;
                btn.addEventListener("click", () => this.navigate(entry.relativePath, ""));
                tdName.appendChild(btn);
            } else {
                tdName.textContent = entry.name;
            }

            const tdType = document.createElement("td");
            tdType.textContent = entry.isDirectory ? "Folder" : "File";

            const tdSize = document.createElement("td");
            tdSize.textContent = entry.isDirectory ? "—" : formatBytes(entry.sizeBytes);

            const tdModified = document.createElement("td");
            tdModified.textContent = entry.lastModifiedUtc
                ? new Date(entry.lastModifiedUtc).toISOString().replace(".000Z", "Z")
                : "—";

            tr.append(tdSelect, tdName, tdType, tdSize, tdModified);
            this.tbody.appendChild(tr);
        }
    }

    async downloadSelected() {
        if (!this.selectedPath || this.selectedIsDirectory) {
            this.#setBrowserStatus("Select a file to download.", "error");
            return;
        }
        try {
            await api.download(this.selectedPath);
            this.#setBrowserStatus("Download started.", "ok");
        } catch (err) {
            this.#setBrowserStatus(err.message, "error");
        }
    }

    async deleteSelected() {
        if (!this.selectedPath) {
            this.#setBrowserStatus("Select a file or folder to delete.", "error");
            return;
        }
        try {
            this.#requireOperator();
            if (!window.confirm(`Delete "${this.selectedPath}"? This cannot be undone.`)) {
                return;
            }
            await api.deletePath(this.selectedPath);
            this.#setBrowserStatus("Deleted.", "ok");
            this.selectedPath = null;
            await this.refresh();
        } catch (err) {
            this.#setBrowserStatus(err.message, "error");
        }
    }

    async copyOrMove(kind) {
        if (!this.selectedPath) {
            this.#setBrowserStatus(`Select a source item to ${kind}.`, "error");
            return;
        }
        try {
            this.#requireOperator();
            const destination = window.prompt(
                `Destination relative path for ${kind}:`,
                `${this.selectedPath}-copy`
            );
            if (!destination) return;

            if (kind === "copy") {
                await api.copyPath(this.selectedPath, destination);
            } else {
                await api.movePath(this.selectedPath, destination);
            }
            this.#setBrowserStatus(`${kind} completed.`, "ok");
            await this.refresh();
        } catch (err) {
            this.#setBrowserStatus(err.message, "error");
        }
    }

    #requireOperator() {
        // use this only as a UX guard; the API's CanWrite policy is authoritative
        if (api.getRole() !== "Operator") {
            throw new Error("Operator token required for this action.");
        }
    }

    #updateWriteButtons() {
        const isOperator = api.getRole() === "Operator";
        const writeButtons = ["#btn-delete", "#btn-copy", "#btn-move"];
        for (const sel of writeButtons) {
            this.dialog.querySelector(sel).disabled = !isOperator || !this.selectedPath;
        }
        this.dialog.querySelector("#btn-download").disabled = !this.selectedPath || this.selectedIsDirectory;
        this.fileInput.disabled = !isOperator;
    }

    #setBrowserStatus(message, kind) {
        this.browserStatus.textContent = message;
        this.browserStatus.className = `status ${kind || ""}`;
    }
}
