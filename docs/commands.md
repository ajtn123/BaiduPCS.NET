# Command reference and migration

This table maps the workflows in [BaiduPCS-Go at `1b91318`](https://github.com/qjfoidnh/BaiduPCS-Go/tree/1b9131817aaf8ca7dee24bc00e33ebc4c7a5cc73) to BaiduPCS.NET. Use the names and options in the BaiduPCS.NET column when updating scripts.

| BaiduPCS-Go | BaiduPCS.NET | Notes |
| --- | --- | --- |
| `login` | `account login` | Use browser cookies with `--cookies` or `--cookies-stdin`, or browser-derived BDUSS with `--bduss` or `--bduss-stdin`. BDUSS sign-in supports optional `--ptoken` and `--stoken`. Password sign-in and its CAPTCHA flow are unsupported. |
| `loglist`, `who`, `su`, `logout` | `account list`, `account current`, `account switch`, `account logout` | Switch by UID, name, or terminal picker. `logout --yes` confirms removal in scripts. |
| `setastoken` | `account set-access-token` | Enter the token at the masked prompt or supply `--stdin`. |
| `quota` | `quota` | Total, used, and free space. |
| `cd`, `pwd` | `cd`, `pwd` | `cd --list` also lists the target. |
| `ls`, `l`, `ll` | `list` | Aliases `ls` and `ll`. Options include `--totals`, `--sort name\|time\|size`, `--descending`, `--folder-sizes`, and `--json`. |
| `search`, `s` | `search` | `--path`, `--recursive`, `--totals`, and `--json`. |
| `tree`, `t`, `meta`, `match` | `tree`, `metadata`, `match` | Alias `meta` for `metadata`. `tree --depth` limits recursion and `--file-ids` displays IDs. `metadata --json` returns full paths and file IDs. |
| `mkdir`, `rm`, `cp`, `mv` | `mkdir`, `remove`, `copy`, `move`, `rename` | Aliases `rm`, `cp`, and `mv`. Copy and move accept multiple sources and a destination. Rename accepts regex replacements with `--pattern`, `--replacement`, and `--dry-run`. |
| `d`, `download` | `download` | Alias `d`. Use `--resume-all` for the active account's queued files, `--stream-discovery` to transfer during traversal, or `--no-preallocate` to write each file progressively. Run `download --help` for transfer options. |
| `lt`, `locate` | `download-links` | Alias `locate`. Add `--pan` for the Pan web endpoint. |
| `u`, `upload` | `upload` | Alias `u`. Pass local sources and a remote directory, or use `upload - REMOTE_FILE` for binary stdin. Use `--as NAME` for a single local source or `--remove-source` for a verified move. |
| `sf`, `sumfile` | `checksum` | Size, MD5, first 256 KiB MD5, CRC32, and checksum-link output. |
| `ep`, `export` | `export` | `--stdout` writes only records. `--out` appends to a file. Use `--format manifest\|checksum-link`, `--root`, `--recursive`, or `--retries`. |
| `share set`, `share list`, `share cancel` | `share create`, `share list`, `share cancel` | Use `--code`, `--days`, `--combined`, and `list --page`. |
| `transfer` | `share import` | Accepts short share URLs, legacy numeric `/share/link` URLs, and copied share text. Use `--destination PATH`, `--code`, `--file-id`, `--collect`, or `--download` as needed. |
| Upstream proposal | `share inspect` | Outputs a JSON inventory for a share, including size totals and whether all directories were listed. |
| `offlinedl`, `od`, `clouddl` | `offline add\|query\|list\|cancel\|delete` | Add accepts `--path`. List accepts `--page`. Deletion supports IDs or `--all`. |
| `recycle` | `recycle list\|restore\|delete` | Use `list --page`. Permanent deletion supports IDs or `--all`. |
| `config`, `config set`, `config reset`, `env` | `config show`, `config set KEY=VALUE...`, `config reset`, `env` | `config show` lists supported kebab-case setting names. Use `BAIDUPCS_CONFIG_DIR` and `BAIDUPCS_VERBOSE`. |
| `run`, `clear`, `cls`, `quit`, `exit` | `run -- EXECUTABLE ARG...`, `clear`, `exit` | Aliases `cls` and `quit`. `shell` starts a command session explicitly. |
| `tool showtime`, `getip`, `enc`, `dec` | `tool time`, `tool ip`, `tool encrypt`, `tool decrypt` | Encrypt and decrypt accept `--method`, `--key-stdin`, `--no-gzip`, `--remove-source`, and `--overwrite`. |
| `update` | `update check`, `update apply` | `apply --yes` skips the prompt. The updater verifies the executable for the current runtime and retains a `.previous` copy. |

On Windows, a helper installs the standalone update after the current process exits. The next launch clears the helper from a completed update. To roll back, close BaiduPCS.NET and restore the `.previous` file to the executable's original name.

`account login --cookies-stdin` accepts a single browser Cookie request header. The optional `Cookie:` prefix, quoted values, comma-separated values, and existing percent escapes are handled during parsing. Cookie values such as session tokens and encoded strings are passed without URL decoding.

## Transfer examples

```sh
baidupcs list /reports --sort size --descending --totals
baidupcs download '/reports/*.csv' --output-directory ./reports --verify
baidupcs upload ./reports /backup --conflict overwrite-if-size-differs
baidupcs export /backup --recursive --root /restored --stdout
```

Direct download is the default. If it is unavailable for the account or region, try `--mode pcs` or `--mode stream`. `--link-index` is zero-based. `--test` reads the data without creating local output. Verification uses metadata hashes where suitable and always validates transfer length. An existing destination is skipped unless `--overwrite` is set.

Downloads and uploads show an updating table in the shell and in one-shot terminal commands. Rows fill from the bottom up. Each active file has a progress bar and shares the table with pending and finished files. On short terminals, rows are selected in priority order from active files, the front of the queue, later queued files, and finished files. The final results include every completed operation. The Transferred, Speed, and ETA columns use fixed widths for the current window, and narrow terminals show fewer columns. Speed and ETA use an average over the last 60 seconds, or the time transferred so far for a new file. Use `--worker-status` for download worker details. Redirected commands show plain results. Use `--raw-numbers` for exact byte counts and durations in seconds, and `--json` on supported commands for machine-readable output.

Directory listing uses the paged Pan endpoint. When Pan rejects the session, the client can fall back to the older PCS listing used by BaiduPCS-Go 4.0.1. If PCS reaches its 1,000-entry limit, sign in with Pan session cookies for a complete listing.

## Batch file operations

```sh
baidupcs list /reports --folder-sizes
baidupcs match '/reports/!(draft*|*.tmp)'
baidupcs rename --pattern '^draft-(.+)\.txt$' --replacement 'final-$1.txt' --dry-run '/reports/draft-*.txt'
```

`--folder-sizes` walks the tree and ranks immediate subdirectories by total file bytes. A negated glob such as `!(draft*|*.tmp)` excludes matches to either alternative within one path component. Batch rename applies the regex to each basename. It checks that sources exist and targets are unique and unused before making changes. Use `--dry-run` to preview the pairs.

## Transfer queue and upload options

```sh
baidupcs download /archive --stream-discovery --output-directory ./archive
baidupcs download --resume-all
baidupcs download /huge.tar --no-preallocate
baidupcs config set skip-rapid-check=true
baidupcs upload ./notes.txt /backup --rapid-check
cat ./archive.tar | baidupcs upload - /backup/archive.tar
baidupcs upload ./verified-copy.tar /backup --remove-source
```

The profile records discovered files before transfer and removes each queue entry when it finishes or is skipped. `--resume-all` resumes those files for the active account using their saved destinations and transfer options. A regular download plans the full tree before starting. With `--stream-discovery`, early files can finish before a later path collision is found. Restart the discovery command to pick up paths that had yet to be found. `--no-preallocate` writes each file in order and can still download several files at once.

`skip-rapid-check=true` skips the rapid-upload probe for future uploads. `--rapid-check` enables it for one invocation. Piped uploads spool binary input to a temporary file so Baidu receives its size and hashes. Use the one-shot CLI for a pipe. For a resumable upload across invocations, pipe to a local file first and upload that file.

`--remove-source` deletes a local file only when Baidu metadata matches both its length and MD5. It applies the same verification to files skipped as already present and keeps the local file if verification fails. It does not traverse links in a selected upload tree for source deletion.

The default save directory contains a subdirectory named for the active account. Set `download-prefix=` to save directly to the configured directory, or `download-prefix=shared` for a shared subdirectory. Use `download-prefix=@account` to restore the per-account layout. `network-family=prefer-ipv6` tries IPv6 first and falls back to IPv4. `network-family=ipv4` restricts connections to IPv4.

An export manifest is newline-delimited JSON. File records contain `type`, `path`, `length`, `md5`, `sliceMd5`, and `crc32`. Empty directories produce `directory` records. `--format checksum-link` emits the Go-compatible `MD5#sliceMD5#length#name` file records. The Go command's inactive rapid-link import and random-rename switch do not provide an available restore workflow.

## Legacy encrypted files

The nine `aes-128`, `aes-192`, and `aes-256` methods with `ctr`, `cfb`, and `ofb` match the legacy IV-prefixed format. Compression is on by default. Specify the same method, key, and gzip setting on decryption. These modes preserve compatibility and do not authenticate ciphertext. `--remove-source` applies after the output is committed. With `--no-gzip`, the format provides no check that a decryption key was correct.

All nine uncompressed modes passed file comparisons in both directions with BaiduPCS-Go 4.0.1 on Windows. Go reported a temporary-file rename error in its default gzip workflow. The intermediate compressed and decompressed bytes were compatible with .NET.
