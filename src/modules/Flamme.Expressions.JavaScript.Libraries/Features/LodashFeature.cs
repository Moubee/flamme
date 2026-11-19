using Flamme.Features.Services;

namespace Flamme.Expressions.JavaScript.Libraries;

public class LodashFeature(IModule module) : ScriptModuleFeatureBase("lodash", module);