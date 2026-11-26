# Command reference and migration

This table maps the workflows in [BaiduPCS-Go at `1b91318`](https://github.com/qjfoidnh/BaiduPCS-Go/tree/1b9131817aaf8ca7dee24bc00e33ebc4c7a5cc73) to BaiduPCS.NET. Use the names and options in the BaiduPCS.NET column when updating scripts.

| BaiduPCS-Go | BaiduPCS.NET | Notes |
| --- | --- | --- |
| `login` | `account login` | Password prompts or existing sessions with `--cookies`, `--cookies-stdin`, `--bduss`, or `--bduss-stdin`. BDUSS supports `--ptoken` and `--stoken`. |
| `loglist`, `who`, `su`, `logout` | `account list`, `account current`, `account switch`, `account logout` | Switch by UID, name, or terminal picker. `logout --yes` confirms removal in scripts. |
| `setastoken` | `account set-access-token` | Enter the token at the masked prompt or supply `--stdin`. |
| `quota` | `quota` | Total, used, and free space. |
| `cd`, `pwd` | `cd`, `pwd` | `cd --list` also lists the target. |
| `ls`, `l`, `ll` | `list` | `--totals`, `--sort name\|time\|size`, `--descending`. |
| `search`, `s` | `search` | `--path`, `--recursive`, and `--totals`. |
| `tree`, `t`, `meta`, `match` | `tree`, `metadata`, `match` | `tree --depth` limits recursion and `--file-ids` displays IDs. |
| `mkdir`, `rm`, `cp`, `mv` | `mkdir`, `remove`, `copy`, `move`, `rename` | Copy and move accept multiple sources and a destination directory. Move also supports one source and a new path. |
| `d`, `download` | `download` | `--mode direct\|pcs\|stream`, `--link-index`, `--parallel`, `--file-concurrency`, `--retries`, `--worker-status`, `--test`, `--overwrite`, `--output-directory`, `--full-path`, `--preserve-mtime`, `--executable`, `--verify`, `--skip-verification`. |
| `lt`, `locate` | `download-links` | Add `--pan` for the Pan web endpoint. |
| `u`, `upload` | `upload` | Pass one or more sources and then the remote directory. Use `--parallel`, `--file-concurrency`, `--retries`, `--skip-rapid-check`, and `--conflict skip\|overwrite\|overwrite-if-size-differs`. |
| `sf`, `sumfile` | `checksum` | Size, MD5, first 256 KiB MD5, CRC32, and checksum-link output. |
| `ep`, `export` | `export` | `--stdout` writes only records. `--out` appends to a file. Use `--format manifest\|checksum-link`, `--root`, `--recursive`, or `--retries`. |
| `share set`, `share list`, `share cancel` | `share create`, `share list`, `share cancel` | Use `--code`, `--days`, `--combined`, and `list --page`. |
| `transfer` | `share import` | Accepts a Baidu share URL or copied text. Use `--code`, repeat `--file-id`, and add `--collect` or `--download` as needed. |
| `offlinedl`, `od`, `clouddl` | `offline add\|query\|list\|cancel\|delete` | Add accepts `--path`. List accepts `--page`. Deletion supports IDs or `--all`. |
| `recycle` | `recycle list\|restore\|delete` | Use `list --page`. Permanent deletion supports IDs or `--all`. |
| `config`, `config set`, `config reset`, `env` | `config show`, `config set KEY=VALUE...`, `config reset`, `env` | `config show` lists supported kebab-case setting names. Use `BAIDUPCS_CONFIG_DIR` and `BAIDUPCS_VERBOSE`. |
| `run`, `clear`, `cls`, `quit`, `exit` | `run -- EXECUTABLE ARG...`, `clear`, `exit` | `shell` starts a command session explicitly. |
| `tool showtime`, `getip`, `enc`, `dec` | `tool time`, `tool ip`, `tool encrypt`, `tool decrypt` | Encrypt and decrypt accept `--method`, `--key-stdin`, `--no-gzip`, `--remove-source`, and `--overwrite`. |
| `update` | `update check`, `update apply` | `apply --yes` skips the prompt. Use `--scope global`, `--scope local`, or `--tool-path PATH` for a .NET tool. A standalone executable updates its matching archive and retains `.previous`. |

## Transfer examples

```sh
baidupcs list /reports --sort size --descending --totals
baidupcs download '/reports/*.csv' --output-directory ./reports --verify
baidupcs upload ./reports /backup --conflict overwrite-if-size-differs
baidupcs export /backup --recursive --root /restored --stdout
```

Direct download is the default. If it is unavailable for the account or region, try `--mode pcs` or `--mode stream`. `--link-index` is zero-based. `--test` reads the data without creating local output. Verification uses metadata hashes where suitable and always validates transfer length. An existing destination is skipped unless `--overwrite` is set.

An export manifest is newline-delimited JSON. File records contain `type`, `path`, `length`, `md5`, `sliceMd5`, and `crc32`. Empty directories produce `directory` records. `--format checksum-link` emits the Go-compatible `MD5#sliceMD5#length#name` file records. The Go command's inactive rapid-link import and random-rename switch do not provide an available restore workflow.

## Legacy encrypted files

The nine `aes-128`, `aes-192`, and `aes-256` methods with `ctr`, `cfb`, and `ofb` match the legacy IV-prefixed format. Compression is on by default. Specify the same method, key, and gzip setting on decryption. These modes preserve compatibility and do not authenticate ciphertext. `--remove-source` applies after the output is committed. With `--no-gzip`, the format provides no check that a decryption key was correct.
