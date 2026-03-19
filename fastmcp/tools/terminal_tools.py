from __future__ import annotations

from . import ProjectContext, parse_args_json, run_process


def register(mcp, context: ProjectContext) -> None:
    @mcp.tool()
    def run_terminal(executable: str, args_json: str = "[]") -> str:
        try:
            args = parse_args_json(args_json)
        except ValueError as exc:
            return str(exc)

        result = run_process(context, [executable, *args])
        if result.returncode != 0:
            return f"command failed (exit code {result.returncode})\n{result.merged or '(no output)'}"

        return result.merged or "command completed"
