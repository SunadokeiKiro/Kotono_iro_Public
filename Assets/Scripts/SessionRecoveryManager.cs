using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq; // Added for Linq

[System.Serializable]
public class PendingSessionList
{
    public List<string> sessionIds = new List<string>();
    public List<string> audioPaths = new List<string>(); // Keep track of associated audio files if needed for re-upload? No, just session ID is enough for recovery if analysis started.
    // Actually, we just need sessionIds to poll.
}

public static class SessionRecoveryManager
{
    private static string FilePath => Path.Combine(Application.persistentDataPath, "pending_sessions.json");

    public static void AddSession(string sessionId)
    {
        var list = LoadList();
        if (!list.sessionIds.Contains(sessionId))
        {
            list.sessionIds.Add(sessionId);
            SaveList(list);
            Debug.Log($"[SessionRecovery] Session {sessionId} added to pending list.");
        }
    }

    public static void RemoveSession(string sessionId)
    {
        var list = LoadList();
        if (list.sessionIds.Contains(sessionId))
        {
            list.sessionIds.Remove(sessionId);
            SaveList(list);
            Debug.Log($"[SessionRecovery] Session {sessionId} removed from pending list.");
        }
    }

    public static List<string> GetPendingSessions()
    {
        return LoadList().sessionIds;
    }

    private static PendingSessionList LoadList()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                string json = File.ReadAllText(FilePath);
                return JsonUtility.FromJson<PendingSessionList>(json) ?? new PendingSessionList();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SessionRecovery] Failed to load list: {e.Message}");
            }
        }
        return new PendingSessionList();
    }

    private static void SaveList(PendingSessionList list)
    {
        try
        {
            string json = JsonUtility.ToJson(list, true);
            File.WriteAllText(FilePath, json);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SessionRecovery] Failed to save list: {e.Message}");
        }
    }
}
