using Hephaestus.Core.Aliases;
using Hephaestus.Core.Invocations;

namespace Hephaestus.Core.Readiness;

/// <summary>How an Agent signs in (FR-25, AD-16): the authentication mode the Owner chose, and when. It is Installation Configuration; the credential is a secret, and is not here.</summary>
public sealed record AgentSetting(Agent Agent, AuthenticationMode Mode, DateTimeOffset ChosenAt);
