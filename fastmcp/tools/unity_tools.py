from __future__ import annotations

import json
import socket
import subprocess
from pathlib import Path

from . import ProjectContext, is_unity_project, json_ok, normalize_relative_path, parse_args_json, write_text


def _send_bridge_request(context: ProjectContext, command: str, *args: str) -> tuple[bool, str, str]:
    payload = {"command": command, "args": list(args)}

    try:
        with socket.create_connection((context.unity_bridge_host, context.unity_bridge_port), timeout=10) as sock:
            sock.sendall((json.dumps(payload, ensure_ascii=False) + "\n").encode("utf-8"))

            received = b""
            while not received.endswith(b"\n"):
                chunk = sock.recv(4096)
                if not chunk:
                    break
                received += chunk

        if not received:
            return False, "Unity bridge returned no response", ""

        response = json.loads(received.decode("utf-8").strip())
        return (
            bool(response.get("success", False)),
            str(response.get("message", "")),
            str(response.get("dataJson", "")),
        )
    except ConnectionRefusedError:
        return (
            False,
            f"Unity bridge connection refused ({context.unity_bridge_host}:{context.unity_bridge_port})",
            "",
        )
    except socket.timeout:
        return False, "Unity bridge timed out", ""
    except Exception as exc:
        return False, f"Unity bridge error: {exc}", ""


def _format_bridge_result(success: bool, message: str, data_json: str) -> str:
    prefix = "[ok]" if success else "[error]"
    if data_json:
        return f"{prefix} {message}\n{data_json}"
    return f"{prefix} {message}"


def _run_unity_method(context: ProjectContext, method_suffix: str, *args: str) -> str:
    command_map = {
        "HealthCheck": "health_check",
        "OpenScene": "open_scene",
        "CreateScene": "create_scene",
        "SaveScene": "save_scene",
        "GetSceneGraph": "get_scene_graph",
        "GetSceneGraphJson": "get_scene_graph_json",
        "CreateGameObject": "create_gameobject",
        "DeleteGameObject": "delete_gameobject",
        "SetParent": "set_parent",
        "SetPosition": "set_position",
        "SetLocalScale": "set_local_scale",
        "AddComponent": "add_component",
        "RemoveComponent": "remove_component",
        "CreatePrefab": "create_prefab",
        "InstantiatePrefab": "instantiate_prefab",
        "SearchAssets": "search_assets",
        "SetInspectorValue": "set_inspector_value",
        "RefreshAssets": "refresh_assets",
        "SyncPauseOptionsChrome": "sync_pause_options_chrome",
        "SyncPlayerWiring": "sync_player_wiring",
        "RunGameplayRegressionChecks": "run_gameplay_regression_checks",
        "StartPlayModeSmokeTest": "start_playmode_smoke_test",
        "GetPlayModeSmokeTestStatus": "get_playmode_smoke_test_status",
        "StartBossUltimateScenarioTest": "start_boss_ultimate_scenario_test",
        "GetBossUltimateScenarioTestStatus": "get_boss_ultimate_scenario_test_status",
        "StartGuardParryBreakScenarioTest": "start_guard_parry_break_scenario_test",
        "GetGuardParryBreakScenarioTestStatus": "get_guard_parry_break_scenario_test_status",
        "StartUiFlowScenarioTest": "start_ui_flow_scenario_test",
        "GetUiFlowScenarioTestStatus": "get_ui_flow_scenario_test_status",
        "StartPauseUiScenarioTest": "start_pause_ui_scenario_test",
        "GetPauseUiScenarioTestStatus": "get_pause_ui_scenario_test_status",
        "StartFullValidationSuite": "start_full_validation_suite",
        "GetFullValidationSuiteStatus": "get_full_validation_suite_status",
        "EnterPlayMode": "enter_playmode",
        "ExitPlayMode": "exit_playmode",
        "CreateBasic3DPlayerRig": "create_basic_3d_player_rig",
    }

    command = command_map.get(method_suffix)
    if command is None:
        return f"[error] unsupported Unity method suffix: {method_suffix}"

    success, message, data_json = _send_bridge_request(context, command, *args)
    return _format_bridge_result(success, message, data_json)


def _create_mono_behaviour_template(class_name: str, namespace: str = "") -> str:
    namespace_open = f"namespace {namespace}\n{{\n" if namespace else ""
    namespace_close = "}\n" if namespace else ""
    indent = "    " if namespace else ""

    return (
        "using UnityEngine;\n\n"
        f"{namespace_open}"
        f"{indent}public sealed class {class_name} : MonoBehaviour\n"
        f"{indent}{{\n"
        f"{indent}    [Header(\"References\")]\n"
        f"{indent}    [SerializeField] private Transform cachedTransform;\n\n"
        f"{indent}    [Header(\"Settings\")]\n"
        f"{indent}    [SerializeField, Min(0f)] private float moveSpeed = 5f;\n\n"
        f"{indent}    private void Reset()\n"
        f"{indent}    {{\n"
        f"{indent}        cachedTransform = transform;\n"
        f"{indent}    }}\n\n"
        f"{indent}    private void Awake()\n"
        f"{indent}    {{\n"
        f"{indent}        if (cachedTransform == null)\n"
        f"{indent}        {{\n"
        f"{indent}            cachedTransform = transform;\n"
        f"{indent}        }}\n"
        f"{indent}    }}\n\n"
        f"{indent}    private void Update()\n"
        f"{indent}    {{\n"
        f"{indent}        // TODO: implement runtime behaviour.\n"
        f"{indent}    }}\n"
        f"{indent}}}\n"
        f"{namespace_close}"
    )


def _create_scriptable_object_template(class_name: str, namespace: str = "") -> str:
    namespace_open = f"namespace {namespace}\n{{\n" if namespace else ""
    namespace_close = "}\n" if namespace else ""
    indent = "    " if namespace else ""

    return (
        "using UnityEngine;\n\n"
        f"{namespace_open}"
        f"{indent}[CreateAssetMenu(fileName = \"{class_name}\", menuName = \"ProjectChuOn/{class_name}\")]\n"
        f"{indent}public sealed class {class_name} : ScriptableObject\n"
        f"{indent}{{\n"
        f"{indent}    [Header(\"Settings\")]\n"
        f"{indent}    [SerializeField] private string descriptionText;\n"
        f"{indent}    [SerializeField, Min(0)] private int value = 1;\n"
        f"{indent}}}\n"
        f"{namespace_close}"
    )


def _create_plain_csharp_template(class_name: str, namespace: str = "") -> str:
    namespace_open = f"namespace {namespace}\n{{\n" if namespace else ""
    namespace_close = "}\n" if namespace else ""
    indent = "    " if namespace else ""

    return (
        "using System;\n\n"
        f"{namespace_open}"
        f"{indent}public sealed class {class_name}\n"
        f"{indent}{{\n"
        f"{indent}}}\n"
        f"{namespace_close}"
    )


def _create_feature_readme(feature_name: str) -> str:
    return (
        f"# {feature_name}\n\n"
        "## Goal\n"
        "- Define the feature scope and ownership.\n\n"
        "## Contents\n"
        "- Runtime scripts\n"
        "- Data assets\n"
        "- Optional editor tooling\n\n"
        "## Checklist\n"
        "- [ ] Create runtime controller\n"
        "- [ ] Create config asset type\n"
        "- [ ] Connect scene references\n"
        "- [ ] Verify in Play Mode\n"
    )


def register(mcp, context: ProjectContext) -> None:
    @mcp.tool()
    def health_check() -> str:
        bridge_success, bridge_message, bridge_data_json = _send_bridge_request(context, "health_check")
        bridge_data: object = bridge_data_json
        if bridge_data_json:
            try:
                bridge_data = json.loads(bridge_data_json)
            except json.JSONDecodeError:
                bridge_data = bridge_data_json

        return json_ok(
            {
                "server_name": context.server_name,
                "server_file": str(context.server_file),
                "server_dir": str(context.server_dir),
                "project_root": str(context.project_root),
                "project_root_valid": is_unity_project(context.project_root),
                "unity_version": context.unity_version,
                "unity_path": context.unity_path,
                "unity_exists": Path(context.unity_path).exists(),
                "unity_api_class": context.unity_api_class,
                "unity_bridge_host": context.unity_bridge_host,
                "unity_bridge_port": context.unity_bridge_port,
                "bridge_success": bridge_success,
                "bridge_message": bridge_message,
                "bridge_data": bridge_data,
                "editor_log_path": str(context.unity_log_path),
                "editor_log_exists": context.unity_log_path.exists(),
            }
        )

    @mcp.tool()
    def get_unity_logs(line_count: int = 200) -> str:
        if not context.unity_log_path.exists():
            return f"Unity Editor.log not found: {context.unity_log_path}"

        lines = context.unity_log_path.read_text(encoding="utf-8", errors="ignore").splitlines()
        return "\n".join(lines[-max(1, line_count) :])

    @mcp.tool()
    def unity_console_errors(line_limit: int = 80) -> str:
        if not context.unity_log_path.exists():
            return f"Unity Editor.log not found: {context.unity_log_path}"

        lines = context.unity_log_path.read_text(encoding="utf-8", errors="ignore").splitlines()
        filtered = [
            line
            for line in lines
            if "error CS" in line
            or "Exception:" in line
            or "NullReferenceException" in line
            or "[FastMCPBridge]" in line
        ]

        if not filtered:
            return "No recent Unity compile errors or exceptions found."

        return "\n".join(filtered[-max(1, line_limit) :])

    @mcp.tool()
    def analyze_compile_errors() -> str:
        raw = unity_console_errors(200)
        if raw.startswith("Unity Editor.log not found") or raw.startswith("No recent Unity"):
            return raw

        diagnostics: list[str] = []
        if "CS0246" in raw:
            diagnostics.append("- CS0246: missing type or namespace reference.")
        if "CS0103" in raw:
            diagnostics.append("- CS0103: name does not exist in the current context.")
        if "CS0117" in raw:
            diagnostics.append("- CS0117: missing member on the referenced type.")
        if "CS1061" in raw:
            diagnostics.append("- CS1061: method or property does not exist on the target type.")
        if "NullReferenceException" in raw:
            diagnostics.append("- NullReferenceException: inspector reference or initialization order issue.")

        if not diagnostics:
            diagnostics.append("- No known pattern matched. Inspect the raw log.")

        return "Compile diagnostics:\n" + "\n".join(diagnostics) + "\n\nRaw log:\n" + raw

    @mcp.tool()
    def open_unity() -> str:
        unity_exe = Path(context.unity_path)
        if not unity_exe.exists():
            return f"Unity executable not found: {context.unity_path}"

        subprocess.Popen([context.unity_path, "-projectPath", str(context.project_root)], cwd=str(context.project_root))
        return "Unity Editor launch requested."

    @mcp.tool()
    def run_unity_method(method_suffix: str, args_json: str = "[]") -> str:
        try:
            args = parse_args_json(args_json)
        except ValueError as exc:
            return str(exc)

        return _run_unity_method(context, method_suffix, *args)

    @mcp.tool()
    def create_unity_script(
        name: str,
        relative_dir: str = "Assets/Scripts/Generated",
        namespace: str = "",
        template_type: str = "monobehaviour",
    ) -> str:
        safe_dir = normalize_relative_path(context, relative_dir)
        script_path = safe_dir / f"{name}.cs"
        if script_path.exists():
            return f"script already exists: {script_path.relative_to(context.project_root)}"

        template_type_lower = template_type.lower()
        if template_type_lower == "monobehaviour":
            content = _create_mono_behaviour_template(name, namespace)
        elif template_type_lower == "scriptableobject":
            content = _create_scriptable_object_template(name, namespace)
        elif template_type_lower == "plain":
            content = _create_plain_csharp_template(name, namespace)
        else:
            return f"unsupported template_type: {template_type}"

        write_text(script_path, content)
        return f"created {script_path.relative_to(context.project_root)}"

    @mcp.tool()
    def scaffold_feature(feature_name: str, base_dir: str = "Assets/Scripts/Features") -> str:
        root = normalize_relative_path(context, base_dir) / feature_name
        runtime_dir = root / "Runtime"
        data_dir = root / "Data"
        editor_dir = root / "Editor"

        runtime_dir.mkdir(parents=True, exist_ok=True)
        data_dir.mkdir(parents=True, exist_ok=True)
        editor_dir.mkdir(parents=True, exist_ok=True)

        runtime_script = runtime_dir / f"{feature_name}Controller.cs"
        data_script = data_dir / f"{feature_name}Config.cs"
        readme_path = root / "README.md"

        if not runtime_script.exists():
            write_text(runtime_script, _create_mono_behaviour_template(f"{feature_name}Controller", "ProjectChuOn"))
        if not data_script.exists():
            write_text(data_script, _create_scriptable_object_template(f"{feature_name}Config", "ProjectChuOn"))
        if not readme_path.exists():
            write_text(readme_path, _create_feature_readme(feature_name))

        return f"feature scaffold created: {root.relative_to(context.project_root)}"

    @mcp.tool()
    def create_game_feature_bundle(feature_name: str) -> str:
        scaffold_result = scaffold_feature(feature_name)
        refresh_result = refresh_assets()
        return f"{scaffold_result}\n{refresh_result}"

    @mcp.tool()
    def create_scene(scene_name: str) -> str:
        return _run_unity_method(context, "CreateScene", scene_name)

    @mcp.tool()
    def open_scene(scene_name: str) -> str:
        return _run_unity_method(context, "OpenScene", scene_name)

    @mcp.tool()
    def save_scene() -> str:
        return _run_unity_method(context, "SaveScene")

    @mcp.tool()
    def get_scene_graph() -> str:
        return _run_unity_method(context, "GetSceneGraph")

    @mcp.tool()
    def get_scene_graph_json() -> str:
        return _run_unity_method(context, "GetSceneGraphJson")

    @mcp.tool()
    def create_gameobject(name: str) -> str:
        return _run_unity_method(context, "CreateGameObject", name)

    @mcp.tool()
    def delete_gameobject(name: str) -> str:
        return _run_unity_method(context, "DeleteGameObject", name)

    @mcp.tool()
    def set_parent(child: str, parent: str) -> str:
        return _run_unity_method(context, "SetParent", child, parent)

    @mcp.tool()
    def set_position(name: str, x: float, y: float, z: float) -> str:
        return _run_unity_method(context, "SetPosition", name, str(x), str(y), str(z))

    @mcp.tool()
    def set_local_scale(name: str, x: float, y: float, z: float) -> str:
        return _run_unity_method(context, "SetLocalScale", name, str(x), str(y), str(z))

    @mcp.tool()
    def add_component(obj: str, component: str) -> str:
        return _run_unity_method(context, "AddComponent", obj, component)

    @mcp.tool()
    def remove_component(obj: str, component: str) -> str:
        return _run_unity_method(context, "RemoveComponent", obj, component)

    @mcp.tool()
    def create_prefab(obj: str, prefab_name: str) -> str:
        return _run_unity_method(context, "CreatePrefab", obj, prefab_name)

    @mcp.tool()
    def instantiate_prefab(prefab: str) -> str:
        return _run_unity_method(context, "InstantiatePrefab", prefab)

    @mcp.tool()
    def search_assets(filter_name: str, search_type: str = "t:Prefab") -> str:
        return _run_unity_method(context, "SearchAssets", filter_name, search_type)

    @mcp.tool()
    def set_inspector_value(obj_name: str, comp_name: str, field: str, value: str) -> str:
        return _run_unity_method(context, "SetInspectorValue", obj_name, comp_name, field, value)

    @mcp.tool()
    def refresh_assets() -> str:
        return _run_unity_method(context, "RefreshAssets")

    @mcp.tool()
    def enter_playmode() -> str:
        return _run_unity_method(context, "EnterPlayMode")

    @mcp.tool()
    def exit_playmode() -> str:
        return _run_unity_method(context, "ExitPlayMode")

    @mcp.tool()
    def create_basic_3d_player_rig(player_name: str = "Player") -> str:
        return _run_unity_method(context, "CreateBasic3DPlayerRig", player_name)
