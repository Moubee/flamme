# Retired Elsa announcement helpers

This archive preserves local announcement drafting and receipt/delivery tests inherited
from Elsa. It is not an agent skill or authorization to contact Elsa accounts.
The `post_discord.py` CLI entry point is disabled before it reads a message or token.
Imported legacy delivery functions exist only to support tests against a local server.

`announcement_pack.py` can draft text from explicitly supplied product, version, and
release links. `buffer_receipt.py` handles local receipt files and does not send posts.
Flamme has no configured social channels or announcement credentials.

See [the archive policy](../README.md) before reusing this code.
