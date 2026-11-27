# Security policy

## Report a vulnerability privately

Send suspected BaiduPCS.NET vulnerabilities through GitHub's private reporting form. Use public issues for ordinary bugs and feature requests. [Report a vulnerability](https://github.com/ajtn123/BaiduPCS.NET/security/advisories/new)

Include the affected version, platform, command or workflow, impact, and steps to reproduce with a synthetic account or test files where possible. Remove passwords, cookies, tokens, profile contents, share codes, and private filenames from logs and attachments. Test only with accounts and files you control.

Use the private report to coordinate disclosure with the maintainers. Include the exact commit if you tested a source build. [Latest stable release](https://github.com/ajtn123/BaiduPCS.NET/releases/latest)

## Keep your account credentials private

`baidupcs env` prints the profile path. The profile stores session credentials, so keep it in a user-private directory. Use masked input or the credential options that read from standard input when available. Keep profile files and credential-bearing command output out of issues, commits, and CI logs.

If a session credential is exposed, invalidate the affected session through Baidu and sign in again.
