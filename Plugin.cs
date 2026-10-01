using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ThirdPersonMod
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class CameraPlugin : BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        // 可调参数（BepInEx配置文件）
        internal static ConfigEntry<float> Sensitivity;   // 鼠标灵敏度
        internal static ConfigEntry<float> CamDist;         // 相机距离
        internal static ConfigEntry<float> OffsetX;         // 水平偏移（负=人物靠右）
        internal static ConfigEntry<float> OffsetY;         // 垂直偏移（正=人物靠下）
        internal static ConfigEntry<float> ConvergeDist;    // 准星汇聚距离
        internal static ConfigEntry<float> RayOriginPush;   // 射线前移校正

        private void Awake()
        {
            Log = Logger;
            Sensitivity = Config.Bind("相机", "鼠标灵敏度", 0.12f, "每像素鼠标位移对应的转向速度");
            CamDist = Config.Bind("相机", "相机距离", 1.4f, "第三人称相机与角色的距离（米）");
            OffsetX = Config.Bind("相机", "水平偏移", -0.6f, "相机向左偏移量，越负人物越靠屏幕右侧");
            OffsetY = Config.Bind("相机", "垂直偏移", 0.4f, "相机向上偏移量，越大人物越靠屏幕下方");
            ConvergeDist = Config.Bind("瞄准", "汇聚距离", 40f, "准星线汇聚到视轴的距离（米），一般不用改");
            RayOriginPush = Config.Bind("瞄准", "射线校正", 0.3f, "拾取手感微调：捡近处物品差一点点就加大");

            new Harmony(PluginInfo.GUID).PatchAll();
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += (ctx, cam) =>
                PatchThirdPerson.EnsureBodyVisible();
            Log.LogInfo("[ThirdPersonMod] 加载完成，B键切换第一/第三人称");
        }
    }

    public static class PluginInfo
    {
        public const string GUID = "camera.patcher.mod";
        public const string Name = "ThirdPersonMod";
        public const string Version = "1.0.0";
    }

    [HarmonyPatch]
    public static class PatchThirdPerson
    {
        private static Type _cameraType;
        private static FieldInfo _followField;
        private static MethodBase _target;
        private static readonly Dictionary<Component, CamState> _states = new Dictionary<Component, CamState>();

        public static CamState CurrentState { get; private set; }
        public static Transform CurrentFollow { get; private set; }

        // 眼睛相对角色锚点的偏移：第一人称时由游戏相机位置自动校准
        public static Vector3 EyeOffset { get; private set; } = Vector3.up * 1.55f;

        public class CamState
        {
            public bool thirdPerson = false;   // 默认第一人称
            public float yaw;
            public float pitch = 12f;
            public float dist;
        }

        public static Type FindCameraType()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                    if (t.Name == "ExampleCharacterCamera") return t;
            }
            return null;
        }

        static bool Prepare()
        {
            _cameraType = FindCameraType();
            if (_cameraType == null) return false;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var f in _cameraType.GetFields(flags))
            {
                if (f.FieldType != typeof(Transform)) continue;
                if (f.Name.Contains("Follow")) { _followField = f; break; }
                if (_followField == null) _followField = f;
            }

            _target = _cameraType.GetMethod("UpdateWithInput", flags)
                      ?? (MethodBase)_cameraType.GetMethod("Update", flags)
                      ?? (MethodBase)_cameraType.GetMethod("LateUpdate", flags);
            return _target != null;
        }

        static MethodBase TargetMethod() => _target;

        private static int? _origMask;
        private static Transform _cachedRoot;
        private static Renderer[] _cachedRenderers;
        private static readonly Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> _origShadowModes =
            new Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();

        // 渲染前恢复角色显示：本地玩家身体被游戏设为ShadowsOnly（只投影不渲染）
        public static void EnsureBodyVisible()
        {
            var st = CurrentState;
            var follow = CurrentFollow;
            if (st == null || follow == null || !st.thirdPerson) return;

            var root = follow.root;
            if (_cachedRenderers == null || _cachedRoot != root)
            {
                _cachedRoot = root;
                _cachedRenderers = root.GetComponentsInChildren<Renderer>(true);
            }
            foreach (var r in _cachedRenderers)
            {
                if (r == null) continue;
                if (!_origShadowModes.TryGetValue(r, out var orig))
                    _origShadowModes[r] = r.shadowCastingMode;
                if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.On)
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

                if (r is SkinnedMeshRenderer smr && !smr.gameObject.activeInHierarchy)
                {
                    smr.gameObject.SetActive(true);
                    var p = smr.transform.parent;
                    while (p != null && p != root && !p.gameObject.activeInHierarchy)
                    {
                        p.gameObject.SetActive(true);
                        p = p.parent;
                    }
                }
            }
        }

        static string GetPath(Transform t)
        {
            var s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        static void Postfix(Component __instance)
        {
            if (_followField == null) return;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (mouse == null || kb == null) return;
            if (Cursor.lockState != CursorLockMode.Locked) return;

            var follow = _followField.GetValue(__instance) as Transform;
            if (follow == null) return;

            if (!_states.TryGetValue(__instance, out var st))
            {
                st = new CamState { yaw = __instance.transform.eulerAngles.y, dist = CameraPlugin.CamDist.Value };
                _states[__instance] = st;
                CameraPlugin.Log.LogInfo($"[ThirdPersonMod] 已接管相机（B键切换），锚定: {follow.name}");
            }
            CurrentState = st;
            CurrentFollow = follow;

            var cam = __instance.GetComponent<Camera>();
            if (cam != null && _origMask == null) _origMask = cam.cullingMask;

            if (kb.bKey.wasPressedThisFrame)
            {
                st.thirdPerson = !st.thirdPerson;
                CameraPlugin.Log.LogInfo(st.thirdPerson ? "[ThirdPersonMod] 第三人称" : "[ThirdPersonMod] 第一人称");
                if (st.thirdPerson)
                {
                    var e = __instance.transform.eulerAngles;
                    st.yaw = e.y;
                    st.pitch = e.x > 180f ? e.x - 360f : e.x;
                }
            }

            if (!st.thirdPerson)
            {
                if (cam != null && _origMask.HasValue) cam.cullingMask = _origMask.Value;
                EyeOffset = __instance.transform.position - follow.position;  // 校准眼睛偏移
                // 第一人称：恢复游戏原本的阴影模式（藏起身体）
                if (_cachedRenderers != null)
                    foreach (var r in _cachedRenderers)
                        if (r != null && _origShadowModes.TryGetValue(r, out var orig))
                            r.shadowCastingMode = orig;
                return;
            }

            // ===== 第三人称 =====
            Vector2 delta = mouse.delta.ReadValue();
            st.yaw += delta.x * CameraPlugin.Sensitivity.Value;
            st.pitch = Mathf.Clamp(st.pitch - delta.y * CameraPlugin.Sensitivity.Value, -35f, 70f);
            st.dist = CameraPlugin.CamDist.Value;

            int addMask = 1 << follow.gameObject.layer;
            foreach (var r in follow.GetComponentsInChildren<Renderer>(true))
                addMask |= 1 << r.gameObject.layer;
            if (cam != null)
                cam.cullingMask = (_origMask ?? cam.cullingMask) | addMask;

            var tr = __instance.transform;
            var rot = Quaternion.Euler(st.pitch, st.yaw, 0f);
            Vector3 pivot = follow.position + EyeOffset;
            Vector3 back = rot * Vector3.back;
            Vector3 off = rot * new Vector3(CameraPlugin.OffsetX.Value, CameraPlugin.OffsetY.Value, 0f);

            float dist = st.dist;
            Vector3 desired = pivot + back * dist + off;
            foreach (var h in Physics.RaycastAll(pivot, (desired - pivot).normalized, dist))
            {
                if (h.collider.isTrigger) continue;
                if (h.collider.transform == follow || h.collider.transform.IsChildOf(follow)) continue;
                dist = Mathf.Min(dist, h.distance);
            }
            dist = Mathf.Max(dist - 0.05f, 0.25f);

            tr.position = pivot + back * dist + off;   // 人物在屏幕右下方
            tr.rotation = rot;
            EnsureBodyVisible();
        }
    }

    [HarmonyPatch(typeof(PlayerInteraction), "InteractionRay")]
    public static class PatchInteractionRay
    {
        private static bool _moved;
        private static Vector3 _pos;
        private static Quaternion _rot;
        private static Camera _gameCam;

        static Camera GetGameCamera()
        {
            if (_gameCam != null) return _gameCam;
            var pi = UnityEngine.Object.FindObjectOfType<PlayerInteraction>();
            if (pi != null)
            {
                var f = typeof(PlayerInteraction).GetField("mainCamera",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _gameCam = f?.GetValue(pi) as Camera;
            }
            if (_gameCam == null) _gameCam = Camera.main;
            return _gameCam;
        }

        static void Prefix()
        {
            var st = PatchThirdPerson.CurrentState;
            var follow = PatchThirdPerson.CurrentFollow;
            if (st == null || follow == null || !st.thirdPerson) return;

            var cam = GetGameCamera();
            if (cam == null) return;

            _pos = cam.transform.position;
            _rot = cam.transform.rotation;

            // 汇聚式瞄准：准星线 = 相机位置 -> 眼睛前方汇聚点，射线与视觉严格一致
            var rot = Quaternion.Euler(st.pitch, st.yaw, 0f);
            Vector3 pivot = follow.position + PatchThirdPerson.EyeOffset;
            Vector3 aimPoint = pivot + rot * Vector3.forward * CameraPlugin.ConvergeDist.Value;
            Vector3 dir = (aimPoint - _pos).normalized;
            float push = Vector3.Distance(_pos, pivot) + CameraPlugin.RayOriginPush.Value;
            cam.transform.SetPositionAndRotation(_pos + dir * push, Quaternion.LookRotation(dir));
            _moved = true;
        }

        static void Postfix()
        {
            if (!_moved) return;
            var cam = GetGameCamera();
            if (cam != null) cam.transform.SetPositionAndRotation(_pos, _rot);
            _moved = false;
        }
    }
}