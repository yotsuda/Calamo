// Lily# Scan Import: carries the OMR reader (bin/omr.exe) and tells the Lily# extension where it is.
// Starts on onStartupFinished (light: no child process) with no dependency on Lily#: VS Code starts a
// dependency first, and Lily#'s start brings up its language server in every window.
// No commands of its own: Lily#'s "Convert Image/PDF to Lily# (.lys)…", "Show Original Scan" and verify on save
// run the reader as a child process (docs/repro/lilysharp-handoff-2026-10-12.md §1.3). Lily# reads
// what activate returns through vscode.extensions.getExtension('yotsuda.lilysharp-omr').exports.
'use strict';

const fs = require('fs');
const path = require('path');
const vscode = require('vscode');

/**
 * Without Lily# this extension shows nothing (the commands are Lily#'s): offer to install it, once
 * (docs/repro/lilysharp-implemented-2026-10-12.md §2.5; an extensionPack would take Lily# away
 * with this one, a dependency would start it in every window).
 */
function offerLilySharp(context) {
    // getExtension only looks: it does not start Lily# (its activation starts a language server).
    if (vscode.extensions.getExtension('yotsuda.lilysharp') || context.globalState.get('lilysharpOffered')) {
        return;
    }
    void context.globalState.update('lilysharpOffered', true);
    void vscode.window.showInformationMessage(
        'Lily# Scan Import runs inside the Lily# extension (Convert Image/PDF to Lily# (.lys)…). Install Lily#?',
        'Install Lily#').then(choice => {
            if (choice === 'Install Lily#') {
                void vscode.commands.executeCommand('workbench.extensions.installExtension', 'yotsuda.lilysharp');
            }
        });
}

/**
 * @returns {{ path: string, version: string, protocol: number } | undefined}
 *   the reader and its `omr --version --json` (written to bin/omr.json when the package is built,
 *   so nothing is started here); undefined where it cannot run (not Windows x64, or bin missing)
 */
function activate(context) {
    if (process.platform !== 'win32' || process.arch !== 'x64') {
        return undefined;
    }
    offerLilySharp(context);
    const bin = path.join(context.extensionPath, 'bin');
    const exe = path.join(bin, 'omr.exe');
    try {
        const info = JSON.parse(fs.readFileSync(path.join(bin, 'omr.json'), 'utf8'));
        if (!fs.existsSync(exe)) {
            return undefined;
        }
        // Lily#'s context key: shows "Convert Image/PDF to Lily# (.lys)… (Beta)" on a scan's right-click before Lily# runs.
        // (Lily#'s own answer replaces it once Lily# starts; this never starts Lily#, whose start brings up its language server:
        // docs/repro/lilysharp-implemented-2026-10-12.md §2.3)
        void vscode.commands.executeCommand('setContext', 'lilysharp.omrAvailable', true);
        return Object.freeze({ path: exe, version: info.version, protocol: info.protocol });
    } catch {
        return undefined;
    }
}

function deactivate() { }

module.exports = { activate, deactivate };
