using System;
using Unity.InferenceEngine;

namespace PoRace {

/// <summary>ONNX policy: float[obsDim] in, float[actDim] out, batch 1, CPU backend (deterministic, ~0.1 ms).</summary>
public sealed class PolicyRunner : IDisposable {
  readonly Worker _worker;
  readonly Tensor<float> _input;
  readonly float[] _inputData;
  readonly float[] _output;
  readonly int _obsDim;

  public PolicyRunner(ModelAsset asset, int obsDim, int actDim) {
    _obsDim = obsDim;
    _inputData = new float[obsDim];
    _output = new float[actDim];
    var model = ModelLoader.Load(asset);
    _worker = new Worker(model, BackendType.CPU);
    _input = new Tensor<float>(new TensorShape(1, obsDim), _inputData);
  }

  public float[] Run(float[] obs, int count) {
    if (count != _obsDim) throw new ArgumentException($"obs {count} != model {_obsDim}");
    _input.Upload(obs);
    _worker.Schedule(_input);
    // CPU backend: the output already lives in CPU memory, so read it in place (no clone or array per inference)
    var result = (Tensor<float>)_worker.PeekOutput();
    result.CompleteAllPendingOperations();
    result.AsReadOnlySpan().Slice(0, _output.Length).CopyTo(_output);
    return _output;
  }

  public void Dispose() {
    _input?.Dispose();
    _worker?.Dispose();
  }
}

}
