using Flamme.Features.Services;

namespace Flamme.Expressions.JavaScript.Libraries;

public class MomentFeature(IModule module) : ScriptModuleFeatureBase("moment", module);