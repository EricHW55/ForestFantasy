using System.Collections.Generic;
using UnityEngine;

public class GoblinGroup : MonoBehaviour
{
    [Header("Group Settings")]
    public float groupMaxRadius = 12f;
    public float maxSeparation = 7f;

    private readonly List<GoblinAI> _members = new List<GoblinAI>();

    public void Register(GoblinAI goblin)
    {
        if (goblin && !_members.Contains(goblin))
            _members.Add(goblin);
    }

    public void Unregister(GoblinAI goblin)
    {
        _members.Remove(goblin);
    }

    public Vector3 GetCenter()
    {
        if (_members.Count == 0) return transform.position;

        Vector3 sum = Vector3.zero;
        int count = 0;

        foreach (var member in _members)
        {
            if (member != null)
            {
                sum += member.transform.position;
                count++;
            }
        }

        return count > 0 ? sum / count : transform.position;
    }

    public List<GoblinAI> GetAvailableMembers(GoblinAI exclude)
    {
        var result = new List<GoblinAI>();
        foreach (var member in _members)
        {
            if (member != null && member != exclude)
                result.Add(member);
        }
        return result;
    }

    public void RaiseAlarm(GoblinAI spotter, Transform target)
    {
        if (!target) return;

        foreach (var member in _members)
        {
            if (member != null)
            {
                bool isLeader = (member == spotter);
                member.OnGroupAlarm(target, isLeader);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
        Gizmos.DrawWireSphere(GetCenter(), groupMaxRadius);

        Gizmos.color = new Color(0f, 1f, 0f, 0.5f);
        Gizmos.DrawSphere(GetCenter(), 0.3f);
    }
}