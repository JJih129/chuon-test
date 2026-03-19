from __future__ import annotations

import json
import os
import shutil
import subprocess
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Iterable


@dataclass(frozen=True)
class ExecResult:
    returncode: int
    stdout: str
    stderr: str

    @property
    def merged(self) -> str:
        if self.stdout and self.stderr:
            return f"{self.stdout}\n{self.stderr}".strip()
        return (self.stdout or self.stderr).strip()


@dataclass(frozen=True)
class ProjectContext:
    server_name: str
    server_file: Path
    server_dir: Path
    project_root: Path
    unity_path: str
    unity_api_class: str
    unity_log_path: Path
    unity_bridge_host: str
    unity_bridge_port: int
    unity_version: str


def is_unity_project(path: Path) -> bool:
    return (
        path.is_dir()
        and (path / "Assets").is_dir()
        and (path / "Packages").is_dir()
        and (path / "ProjectSettings").is_dir()
    )


def candidate_project_roots(server_dir: Path) -> Iterable[Path]:
    env_root = os.getenv("UNITY_PROJECT_ROOT")
    if env_root:
        yield Path(env_root).expanduser().resolve()

    yield server_dir.parent.resolve()
    yield Path.cwd().resolve()
    yield server_dir.resolve()
    yield server_dir.parent.parent.resolve()


def find_project_root(server_dir: Path) -> Path:
    for candidate in candidate_project_roots(server_dir):
        if is_unity_project(candidate):
            return candidate

    return server_dir.parent.resolve()


def parse_project_version(project_root: Path) -> str:
    version_file = project_root / "ProjectSettings" / "ProjectVersion.txt"
    if not version_file.exists():
        return ""

    for line in version_file.read_text(encoding="utf-8", errors="ignore").splitlines():
        if line.startswith("m_EditorVersion:"):
            return line.split(":", 1)[1].strip()

    return ""


def resolve_unity_path(project_root: Path) -> str:
    env_path = os.getenv("UNITY_PATH")
    if env_path:
        return env_path

    version = parse_project_version(project_root)
    candidates: list[Path] = []

    if version:
        candidates.extend(
            [
                Path(rf"C:\Program Files\Unity\Hub\Editor\{version}\Editor\Unity.exe"),
                Path(rf"D:\Unity\{version}\Editor\Unity.exe"),
                Path(rf"C:\Unity\{version}\Editor\Unity.exe"),
            ]
        )

    candidates.extend(
        [
            Path(r"C:\Program Files\Unity\Hub\Editor\Unity.exe"),
            Path(r"D:\Unity\Editor\Unity.exe"),
            Path(r"C:\Unity\Editor\Unity.exe"),
        ]
    )

    for candidate in candidates:
        if candidate.exists():
            return str(candidate)

    return str(candidates[0]) if candidates else "Unity.exe"


def create_context(server_name: str, server_file: Path) -> ProjectContext:
    server_file = server_file.resolve()
    server_dir = server_file.parent
    project_root = find_project_root(server_dir)

    return ProjectContext(
        server_name=server_name,
        server_file=server_file,
        server_dir=server_dir,
        project_root=project_root,
        unity_path=resolve_unity_path(project_root),
        unity_api_class=os.getenv("UNITY_API_CLASS", "FastMCPUnityAPI"),
        unity_log_path=Path(
            os.getenv(
                "UNITY_EDITOR_LOG_PATH",
                str(Path.home() / "AppData" / "Local" / "Unity" / "Editor" / "Editor.log"),
            )
        ),
        unity_bridge_host=os.getenv("UNITY_BRIDGE_HOST", "127.0.0.1"),
        unity_bridge_port=int(os.getenv("UNITY_BRIDGE_PORT", "18777")),
        unity_version=parse_project_version(project_root),
    )


def normalize_relative_path(context: ProjectContext, relative_path: str) -> Path:
    candidate = (context.project_root / relative_path).resolve()

    try:
        candidate.relative_to(context.project_root)
    except ValueError as exc:
        raise ValueError(f"path is outside project root: {relative_path}") from exc

    return candidate


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8", newline="\n")


def backup_file(context: ProjectContext, path: Path) -> Path:
    backup_dir = context.project_root / ".fastmcp_backups"
    backup_dir.mkdir(parents=True, exist_ok=True)

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S_%f")
    backup_path = backup_dir / f"{path.name}.{timestamp}.bak"
    shutil.copy2(path, backup_path)
    return backup_path


def run_process(context: ProjectContext, command: list[str], cwd: Path | None = None) -> ExecResult:
    result = subprocess.run(
        command,
        cwd=str(cwd or context.project_root),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    return ExecResult(
        returncode=result.returncode,
        stdout=result.stdout.strip(),
        stderr=result.stderr.strip(),
    )


def json_ok(data: object) -> str:
    return json.dumps(data, ensure_ascii=False, indent=2)


def parse_args_json(args_json: str) -> list[str]:
    try:
        parsed = json.loads(args_json)
    except json.JSONDecodeError as exc:
        raise ValueError(f"failed to parse args_json: {exc}") from exc

    if not isinstance(parsed, list) or not all(isinstance(item, str) for item in parsed):
        raise ValueError("args_json must be a JSON array of strings")

    return parsed
