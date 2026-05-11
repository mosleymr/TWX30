# Changelog

## Unreleased

### TWX Script Commands

- Added `GETCOURSES <array> <source> <destination>` as the final command in the native command table so existing compiled command IDs are not shifted.
- `GETCOURSES` returns every unique shortest directed course between the source and destination using the active database and current avoid list.
- The returned variable is a two-dimensional array. The outer array scalar is the number of courses returned. Each outer element is itself a `GETCOURSE`-style path array: its scalar value is the hop count, and indexes `1..n` contain the sectors from source through destination.
- Result ordering is seeded with the legacy TWX/Pascal breadth-first path first, followed by the current `GETCOURSE` bidirectional path when that path is different. Remaining equal-hop courses are then enumerated in stored warp order. If the legacy and current paths are identical, that route appears only once.
- Removed the experimental `GETCOURSEDIJKSTRA` script command and switched MTC route display callers away from the old Dijkstra path helper.
