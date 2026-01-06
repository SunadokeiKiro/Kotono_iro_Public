using UnityEngine;
using System;

[Obsolete("Use FirestoreManager instead. Local file storage is deprecated.")]
public static class ArtDataIO
{
    public static ArtData Load(string monthKey)
    {
        Debug.LogError("ArtDataIO.Load is deprecated and should not be used. Please use FirestoreManager.");
        return new ArtData();
    }
}
