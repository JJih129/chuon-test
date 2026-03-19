from __future__ import annotations

from pathlib import Path

from fastmcp import FastMCP

if __package__ in {None, ""}:
    import sys

    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from tools import create_context, is_unity_project
    from tools.file_tools import register as register_file_tools
    from tools.git_tools import register as register_git_tools
    from tools.terminal_tools import register as register_terminal_tools
    from tools.unity_tools import register as register_unity_tools
else:
    from .tools import create_context, is_unity_project
    from .tools.file_tools import register as register_file_tools
    from .tools.git_tools import register as register_git_tools
    from .tools.terminal_tools import register as register_terminal_tools
    from .tools.unity_tools import register as register_unity_tools


SERVER_FILE = Path(__file__).resolve()
CONTEXT = create_context("project-chuon-dev-server", SERVER_FILE)

mcp = FastMCP(CONTEXT.server_name)

register_file_tools(mcp, CONTEXT)
register_git_tools(mcp, CONTEXT)
register_terminal_tools(mcp, CONTEXT)
register_unity_tools(mcp, CONTEXT)


if __name__ == "__main__":
    if not is_unity_project(CONTEXT.project_root):
        raise RuntimeError(
            "Resolved project root is not a valid Unity project.\n"
            f"- project_root: {CONTEXT.project_root}"
        )

    mcp.run()
