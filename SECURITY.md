# Security Policy

## Supported versions
ImmersiveX is pre-1.0. Security fixes go to the `main` branch only.

## Reporting a vulnerability
Please **don't open a public issue**. Use GitHub's private reporting instead:
**Security ▸ Report a vulnerability** on this repository, or open [a new advisory](https://github.com/OlafenwaMoses/ImmersiveX/security/advisories/new).

Please include:
- what the issue is and what an attacker could do with it
- steps or a proof of concept to reproduce it
- the affected platform(s) and version or commit

You can expect an acknowledgement within a week and a status update when a fix is planned.

## Privacy-sensitive areas
ImmersiveX handles room scans and spatial anchors, which reveal the layout of people's homes and workplaces.
We especially want to hear about anything that could:
- leak room data off the device
- expose saved anchors or media caches to other apps
- load remote media without HTTPS
