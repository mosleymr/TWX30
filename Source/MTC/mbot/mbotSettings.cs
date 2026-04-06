using System;
using Core = TWXProxy.Core;

namespace MTC.mbot;

internal sealed record mbotSettings(
    string BotName,
    string TeamName,
    string LoginPassword,
    string BotPassword,
    int SubspaceChannel)
{
    public static mbotSettings Load()
    {
        string botName = Read("$BOT~BOT_NAME", "mbot");
        string teamName = Read("$BOT~BOT_TEAM_NAME", botName);
        string loginPassword = Read("$BOT~PASSWORD", string.Empty);
        string botPassword = Read("$BOT~BOT_PASSWORD", string.Empty);
        int subspace = ParseInt(Read("$BOT~SUBSPACE", "0"));

        if (string.IsNullOrWhiteSpace(teamName))
            teamName = botName;

        if (string.IsNullOrWhiteSpace(botPassword) && subspace > 0)
            botPassword = subspace.ToString();

        return new mbotSettings(botName, teamName, loginPassword, botPassword, subspace);
    }

    private static string Read(string name, string fallback)
    {
        string value = Core.ScriptRef.GetCurrentGameVar(name, fallback);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, out int result) ? result : 0;
    }
}
