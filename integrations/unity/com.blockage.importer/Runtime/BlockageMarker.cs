using UnityEngine;

namespace Blockage
{
    /// <summary>What a Blockage marker marks.</summary>
    public enum BlockageMarkerKind
    {
        /// <summary>A named point: a place and a turn for the game to use.</summary>
        Empty,

        /// <summary>Where a player or a creature appears, facing the way it points.</summary>
        Spawn,

        /// <summary>A box something happens in when it is entered; it comes with a trigger collider.</summary>
        Trigger,

        /// <summary>Where a sound comes from, heard as far as its size.</summary>
        Sound,
    }

    /// <summary>
    /// A marker from Blockage — a spawn point, a trigger box, a sound — at its place and turn, with
    /// its size in metres: the box a trigger fills, standing on the marker, or how far a sound carries.
    /// </summary>
    public sealed class BlockageMarker : MonoBehaviour
    {
        public BlockageMarkerKind kind;

        public Vector3 size = Vector3.one;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.43f, 0.78f, 0.94f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            switch (kind)
            {
                case BlockageMarkerKind.Trigger:
                    Gizmos.DrawWireCube(new Vector3(0f, size.y * 0.5f, 0f), size);
                    break;
                case BlockageMarkerKind.Sound:
                    Gizmos.DrawWireSphere(Vector3.zero, size.x);
                    break;
                case BlockageMarkerKind.Spawn:
                    Gizmos.DrawWireSphere(Vector3.zero, Mathf.Max(size.x, size.z) * 0.5f);
                    Gizmos.DrawLine(Vector3.zero, Vector3.forward * Mathf.Max(size.x, size.z));
                    break;
                default:
                    Gizmos.DrawLine(Vector3.left * size.x, Vector3.right * size.x);
                    Gizmos.DrawLine(Vector3.down * size.x, Vector3.up * size.x);
                    Gizmos.DrawLine(Vector3.back * size.x, Vector3.forward * size.x);
                    break;
            }
        }
    }
}
