#!/usr/bin/env python3
"""Piper's ONNX export on the legacy (TorchScript) exporter.

torch 2.13 defaults `torch.onnx.export` to the dynamo path, which traces VITS
symbolically and dies on a data-dependent assert in the model's own code
(`assert (discriminant >= 0).all()`). Piper's exporter passes no `dynamo=` argument, so
the default decides; this pins the working one without editing the installed package.
"""
import functools
import runpy
import sys

import torch

torch.onnx.export = functools.partial(torch.onnx.export, dynamo=False)
sys.argv[0] = "piper.train.export_onnx"
runpy.run_module("piper.train.export_onnx", run_name="__main__")
