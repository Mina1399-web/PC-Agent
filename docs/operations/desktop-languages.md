# Desktop interface language

This development increment follows the immutable `v1.2.8-preview.1` release.
It does not add language selection to that already published package. Use a
new build containing this change; the installed 1.2.8 preview is unchanged.

The **Interface language** selector is in the desktop header. Its choices
are **Čeština** (`cs-CZ`), **English** (`en`) and **Deutsch** (`de`). It supports
keyboard focus and has a translated accessibility name. Selecting a language
immediately updates the desktop labels, navigation, status, incident details,
recommendations, maintenance window, tray menu, product-owned dialogs and
number/date formats. The currently selected rows and typed operation/approval
identities are retained. A language change does not fetch new data, recreate a
maintenance preview or approve an action.

On the first run, the desktop uses the Windows account's UI language if its
family is Czech, English or German; other languages use English. A saved
choice takes precedence on subsequent runs. The choice is stored as
`uiLanguage` in `%LOCALAPPDATA%\HostAgent\desktop.json`, using the project's
JSON configuration and atomic replacement approach in a separate per-user
desktop scope. It requires no elevation and preserves other JSON properties.
No service or helper configuration is read or written by this preference.

If settings cannot be read, the desktop uses the first-run language rule. An
unsupported saved language falls back to English. If a new choice cannot be
saved, the previous language remains active and the desktop displays an
error. Malformed settings are preserved rather than overwritten; repair the
per-user desktop settings/access before retrying. The preference must not be
placed in the maintenance Quarantine directory.

Selecting a language changes presentation only. Shadow/read-only, network
options, service state, permissions, allowed operations and exact approval
requirements are unchanged. Protocol identifiers, diagnostic/health codes,
GUIDs, PID/process identities, filenames, hashes and JSON export fields remain
stable data. Known states/reasons receive translated descriptions; technical
codes remain visible where useful for support. UTC evidence stays labelled
as UTC while its date presentation follows the selected language.

Windows Settings, UAC and the separately launched approval CLI retain their
own language. The desktop does not change Windows language settings or the
CLI approval contract. Product-owned confirmation and export-path dialogs
use the chosen desktop language, including their buttons.

## Adding another language

Translations live in `src/HostAgent.Desktop/Resources/UiText.resx` (neutral
English), `UiText.cs-CZ.resx` and `UiText.de.resx`. Keep stable resource keys,
source comments, composite placeholders, intentional line breaks and boundary
whitespace. Stable named resource keys are the lookup contract for both XAML
and code-built text; Czech source comments assist translation without binding
the code to a particular translated wording. New messages need entries in
every catalog; new languages also need an
allowlisted selector choice and a Windows-language resolution rule.

Run Desktop localization/UI tests in the approved DEV VM. They use synthetic
View responses, isolated preference files and bounded maintenance fixtures;
they do not require MSI, elevation or service control. A future release must
package its satellite resource assemblies and pass its own artifact/signing
checks. Do not relabel the published 1.2.8 assets or describe this feature as
available in them.
