---
name: read-vs-logs
description: Reads Vintage Story logs after manual testing by the user.
---

# Read Vintage Story logs

On Mac: Read from ~/Library/Application Support/VintageStoryData/Logs. Be sure to quote the space correctly.
On Linux: Read from ~/.config/VintageStoryData/Logs.

There are multiple files, from both the integrateed server and client-side. Log entries are split across them unpredictably, so always use rg or grep.
