This folder contains a trivial example of how the AntiSO library can be used. The example consists of 2 project:

- Sample.Lib project defines some recursive functions (namely GCD and Fibonacci) for which stack-safe versions are generated. 
- Sample.App project uses those functions.

Note that there is no requirement of putting all the processed functions into a separate library. This example just shows that while Sample.Lib depends on the AntiSO.CodeGen analyzer assembly + AntiSO.Shared assembly, Sample.App only has AntiSO.Shared as its (transitive) runtime dependency. It means a library containing some generated code can be distributed without a need to run the generator by its consumers.

Note that there is no dependency on the main project itself. To build the sample project you need to put the AntiSO.Shared and AntiSO.CodeGen assemblies into the CompiledLib folder. Building the main project in the Release mode does it automatically. 

For more details on the AntiSO library see the [README.md](../README.md) in the parent folder.