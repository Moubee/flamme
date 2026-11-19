using Flamme.Features.Services;

namespace Flamme.Expressions.JavaScript.Libraries;

public class LodashFpFeature(IModule module) : ScriptModuleFeatureBase("lodashFp", module);