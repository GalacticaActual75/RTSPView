# Optional HTTPS administration

This advanced guide is optional. Version 1.0.34 adds a simpler System → LAN access switch for trusted local networks. Use this guide when you specifically want encrypted HTTPS administration. Custom URL/endpoint settings take precedence over that switch.

LAN access is optional. By default, the admin panel is available only at `http://127.0.0.1:5080` on the RTSPView computer. On another computer or phone, `localhost` and `127.0.0.1` refer to that device, not the RTSPView host. Opening a firewall port alone does not enable remote access.

Complete initial setup and change the default password locally first. Then configure a trusted HTTPS endpoint using the steps below. These settings configure the Controller's web server; they do not belong in `settings.json` and are not loaded from a `.env` file.

1. Choose a stable LAN hostname for the RTSPView computer and make sure your other devices can resolve it to that computer's LAN address. A DHCP reservation can help keep the address stable. In the example below, **replace `rtspview.example` with your actual hostname**; it is only a placeholder.
2. Obtain a server certificate whose Subject Alternative Name includes that hostname, with its private key, from a certificate authority trusted by your client devices. A private CA works if its root is installed and trusted on those devices. Import the server certificate into **Current User → Personal → Certificates** for the Windows account running RTSPView (`certmgr.msc`). The certificate must be valid for server authentication and that account must be able to use its private key. The example assumes its subject contains the chosen hostname. Do not commit certificates/private keys or bypass browser certificate warnings.
3. Open PowerShell as that same Windows user and set the persistent user environment variables below. This retains local HTTP access on 5080 and adds HTTPS on 5081. `AllowedHosts` contains hostnames only, without schemes or ports. Replace both hostname placeholders before running:

```powershell
[Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', 'http://127.0.0.1:5080;https://0.0.0.0:5081', 'User')
[Environment]::SetEnvironmentVariable('AllowedHosts', 'localhost;127.0.0.1;[::1];rtspview.example', 'User')
[Environment]::SetEnvironmentVariable('Kestrel__Certificates__Default__Subject', 'rtspview.example', 'User')
[Environment]::SetEnvironmentVariable('Kestrel__Certificates__Default__Store', 'My', 'User')
[Environment]::SetEnvironmentVariable('Kestrel__Certificates__Default__Location', 'CurrentUser', 'User')
[Environment]::SetEnvironmentVariable('Kestrel__Certificates__Default__AllowInvalid', 'false', 'User')
```

The certificate-store configuration uses ASP.NET Core's [Kestrel HTTPS configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-8.0). No certificate password is needed in this example because the private key is accessed through the Windows certificate store. HTTPS startup fails if the configured certificate cannot be found or used.

4. On a trusted LAN, confirm the Windows network connection uses the **Private** profile. In an **administrator PowerShell** window, allow the example HTTPS port from the local subnet:

```powershell
New-NetFirewallRule -DisplayName 'RTSPView Admin HTTPS' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5081 -RemoteAddress LocalSubnet -Profile Private
```

The installer's existing port-5080 rule does not open port 5081. If you choose another HTTPS port, change both the binding and firewall rule. Routed management networks need a deliberately scoped source-subnet rule; the example only allows the local subnet. Do not add a router port-forward for the admin panel.

5. Sign out and back in so the application and startup task inherit the updated environment. If automatic startup is disabled, launch RTSPView after signing back in. Restarting only the Viewer does not restart the Controller or change its listener.
6. From another device on the permitted LAN, open **`https://rtspview.example:5081`**, substituting your hostname, and sign in with your changed admin password. Use that HTTPS address for remote administration. The local Web configuration shortcut still opens the loopback address. Update installation and other Windows elevation prompts still require approval on the RTSPView computer.

For access by IP address instead of hostname, the certificate must contain that IP as an IP Subject Alternative Name and the exact address must also appear in `AllowedHosts`. A certificate for a DNS name does not automatically validate an IP-address URL. Never browse to `0.0.0.0`: it is the listener binding, not the host's address.

If access fails:

- **Timeout/refused connection:** check Controller startup, the chosen port, firewall/network profile, DNS, and Wi-Fi client isolation. From another Windows computer, `Test-NetConnection rtspview.example -Port 5081` checks connectivity after substituting your hostname.
- **HTTP 400:** check the exact requested hostname/IP is listed in `AllowedHosts`, then restart with the updated environment.
- **Certificate warning:** check the hostname, expiry and issuing CA's trust on the client; correct the certificate/trust configuration rather than clicking through the warning.
- **Local access works but LAN access does not:** confirm the Controller inherited the HTTPS binding, not just the default loopback URL. HTTP access to the host's LAN address on 5080 remains disabled in this example.

To return to local-only access, set `ASPNETCORE_URLS` back to `http://127.0.0.1:5080` and `AllowedHosts` back to `localhost;127.0.0.1;[::1]` in the user environment, sign out/in, and remove the `RTSPView Admin HTTPS` firewall rule if no longer needed.
