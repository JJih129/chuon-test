from __future__ import annotations

import re

from . import ProjectContext, backup_file, json_ok, normalize_relative_path, read_text, write_text


def register(mcp, context: ProjectContext) -> None:
    @mcp.tool()
    def get_project_root() -> str:
        return str(context.project_root)

    @mcp.tool()
    def summarize_project_structure(max_depth: int = 3) -> str:
        lines: list[str] = []

        def walk(path, depth: int) -> None:
            if depth > max_depth:
                return

            try:
                entries = sorted(path.iterdir(), key=lambda item: (item.is_file(), item.name.lower()))
            except OSError:
                return

            for entry in entries:
                try:
                    relative = entry.relative_to(context.project_root)
                except ValueError:
                    continue

                lines.append(f"{'  ' * depth}{relative}")
                if entry.is_dir():
                    walk(entry, depth + 1)

        for root_name in ("Assets", "Packages", "ProjectSettings"):
            root_path = context.project_root / root_name
            if root_path.exists():
                lines.append(root_name)
                walk(root_path, 1)

        return "\n".join(lines[:3000])

    @mcp.tool()
    def read_file(path: str) -> str:
        full_path = normalize_relative_path(context, path)
        if not full_path.exists():
            return f"file not found: {path}"
        return read_text(full_path)

    @mcp.tool()
    def write_file(path: str, content: str, create_backup: bool = False) -> str:
        full_path = normalize_relative_path(context, path)

        backup_path = None
        if create_backup and full_path.exists():
            backup_path = backup_file(context, full_path)

        write_text(full_path, content)
        if backup_path is not None:
            return f"wrote {path}\nbackup: {backup_path}"
        return f"wrote {path}"

    @mcp.tool()
    def modify_file(path: str, search: str, replace: str, create_backup: bool = True) -> str:
        full_path = normalize_relative_path(context, path)
        if not full_path.exists():
            return f"file not found: {path}"

        content = read_text(full_path)
        if search not in content:
            return f"search text not found: {search}"

        backup_path = backup_file(context, full_path) if create_backup else None
        write_text(full_path, content.replace(search, replace))

        if backup_path is not None:
            return f"modified {path}\nbackup: {backup_path}"
        return f"modified {path}"

    @mcp.tool()
    def regex_patch_file(path: str, pattern: str, replacement: str, create_backup: bool = True) -> str:
        full_path = normalize_relative_path(context, path)
        if not full_path.exists():
            return f"file not found: {path}"

        content = read_text(full_path)
        updated, count = re.subn(pattern, replacement, content, flags=re.MULTILINE)
        if count == 0:
            return f"no matches for pattern: {pattern}"

        backup_path = backup_file(context, full_path) if create_backup else None
        write_text(full_path, updated)

        if backup_path is not None:
            return f"patched {path}\nreplacements: {count}\nbackup: {backup_path}"
        return f"patched {path}\nreplacements: {count}"

    @mcp.tool()
    def search_files(pattern: str) -> str:
        matches = [str(path.relative_to(context.project_root)) for path in context.project_root.glob(pattern)]
        matches.sort()
        return json_ok(matches)

    @mcp.tool()
    def search_text_in_files(glob_pattern: str, keyword: str, max_results: int = 100) -> str:
        results: list[dict[str, object]] = []

        for file_path in context.project_root.glob(glob_pattern):
            if not file_path.is_file():
                continue

            try:
                lines = file_path.read_text(encoding="utf-8", errors="ignore").splitlines()
            except OSError:
                continue

            for line_number, line in enumerate(lines, start=1):
                if keyword not in line:
                    continue

                results.append(
                    {
                        "file": str(file_path.relative_to(context.project_root)),
                        "line": line_number,
                        "text": line.strip(),
                    }
                )

                if len(results) >= max_results:
                    return json_ok(results)

        return json_ok(results)
