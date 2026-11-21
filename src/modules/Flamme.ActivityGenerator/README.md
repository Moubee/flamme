# Flamme activity generator

Add `Flamme.ActivityGenerator` as a private package reference to a project that
declares Flamme activities. Pin it to the same release as `Flamme.Workflows.Core`.
The generated module contains catalog descriptors, member accessors, child
ports, construction factories, and bindings for named activity callbacks.

Consumers register the producer assembly or select a declared activity context.
See [activity authoring](https://github.com/Moubee/flamme/blob/main/doc/wiki/activities-and-authoring.md#producer-contexts)
for an example of selecting a closed generic activity in a consuming project.
