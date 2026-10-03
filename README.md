# Socket Mobile Grocery Scanner

A .NET MAUI app for scanning groceries with a Socket Mobile barcode scanner,
tracking inventory, maintaining a shopping list with two-way Microsoft To Do
sync, and automatically translating/filling in product images for free.

## Setup

### 1. SocketMobile Capture SDK credentials

This project does **not** include the real `AndroidManifest.xml` because it
contains vendor-issued SDK credentials (`APP_KEY` / `DEVELOPER_ID`) tied to
this app's package name and signing certificate.

1. Copy the template: `Platforms/Android/AndroidManifest.xml.sample` → `Platforms/Android/AndroidManifest.xml`
2. Register your package name + signing certificate fingerprint at the
   [SocketMobile Developer Portal](https://developer.socketmobile.com) to get
   your own `APP_KEY` and `DEVELOPER_ID`.
3. Fill in those values in your local (gitignored) `AndroidManifest.xml`.

### 2. Microsoft To Do sync (MSAL)

The app uses a public-client Microsoft Entra ID app registration
(`MsalAuthService.cs`) for Microsoft To Do sign-in. The `ClientId` in that
file is a public, non-confidential identifier (no client secret is used, per
MSAL public-client design) — to use your own Entra ID app registration
instead, replace it with your own app's client ID.

### 3. Build

```
dotnet build -f net10.0-android
dotnet build -f net10.0-windows10.0.19041.0
```

Release Android build:

```
dotnet publish -f net10.0-android -c Release -p:AndroidPackageFormat=apk
```
