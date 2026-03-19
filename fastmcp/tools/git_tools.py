from __future__ import annotations

from . import ProjectContext, run_process


def register(mcp, context: ProjectContext) -> None:
    @mcp.tool()
    def git_status() -> str:
        result = run_process(context, ["git", "status", "--short", "--branch"])
        return result.merged or "(no output)"

    @mcp.tool()
    def git_commit(message: str) -> str:
        add_result = run_process(context, ["git", "add", "."])
        if add_result.returncode != 0:
            return f"git add failed\n{add_result.merged or '(no output)'}"

        commit_result = run_process(context, ["git", "commit", "-m", message])
        if commit_result.returncode != 0:
            return f"git commit failed\n{commit_result.merged or '(no output)'}"

        return f"git commit complete\n{commit_result.merged or '(no output)'}"
