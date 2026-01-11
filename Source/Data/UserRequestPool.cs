using System.Collections.Generic;
using Verse;

namespace RimTalk.Data;

public class UserRequestPool
{
    private static readonly List<Pawn> UserRequests = [];

    public static void Add(Pawn pawn, bool priority = false)
    {
        if (priority)
            UserRequests.Insert(0, pawn);
        else
            UserRequests.Add(pawn);
    }

    public static Pawn GetNextUserRequest()
    {
        return UserRequests.Count == 0 ? null : UserRequests[0];
    }

    public static void Remove(Pawn pawn)
    {
        UserRequests.Remove(pawn);
    }
    
    public static void Clear()
    {
        UserRequests.Clear();
    }
}