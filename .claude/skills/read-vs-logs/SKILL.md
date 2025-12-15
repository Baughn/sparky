---
name: read-vs-logs
description: Reads Vintage Story logs after manual testing by the user.
---

# Read Vintage Story logs

On Mac: Grep ~/Library/Application Support/VintageStoryData/Logs. Be sure to quote the space correctly.
On Linux: Grep ~/.config/VintageStoryData/Logs.

## Example

```bash
grep -a "Sparky" "~/Library/Application Support/VintageStoryData/Logs/*.log"
```
