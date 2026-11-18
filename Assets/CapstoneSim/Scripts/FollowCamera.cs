// FollowCamera.cs
// 3인칭 추적 시점 ↔ 운전석 1인칭 시점 전환 카메라.
// 데이터 수집 시에는 실제 운전과 비슷한 '운전석 시점'을 권장한다.
using UnityEngine;

namespace CapstoneSim
{
    public class FollowCamera : MonoBehaviour
    {
        public enum Mode { Driver, Chase }

        public Transform target;
        public Mode mode = Mode.Driver;

        [Header("추적 시점")]
        public float distance = 6.5f;
        public float height = 2.4f;
        public float followSharpness = 6f;

        [Header("운전석 시점 (차량 로컬 좌표, 좌핸들)")]
        public Vector3 driverEye = new Vector3(-0.37f, 1.2f, 0.05f);
        public float driverPitch = 4f;

        public void ToggleMode() => mode = mode == Mode.Driver ? Mode.Chase : Mode.Driver;

        void LateUpdate()
        {
            if (!target) return;
            if (mode == Mode.Driver)
            {
                transform.SetPositionAndRotation(
                    target.TransformPoint(driverEye),
                    target.rotation * Quaternion.Euler(driverPitch, 0f, 0f));
                return;
            }

            Vector3 fwd = target.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 desired = target.position - fwd * distance + Vector3.up * height;
            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, k);
            transform.rotation = Quaternion.LookRotation(target.position + fwd * 4f + Vector3.up * 1f - transform.position, Vector3.up);
        }
    }
}
