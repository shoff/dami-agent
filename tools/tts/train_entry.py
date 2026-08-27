#!/usr/bin/env python3
"""Piper's VITS trainer with one checkpoint callback.

Piper's own entry point registers two: best-by-`val_mel`, and best-by-`val_mos`. The
second needs the UTMOS predictor, which loads a model over the network; this host does
not fetch models mid-training, so `val_mos` is never logged — and this Lightning version
raises `MisconfigurationException` on a monitored key that never appears rather than
skipping the callback, which is what piper's comment assumes. Passing a replacement on
the command line appends instead of replacing, so the trainer is constructed here with
exactly the callback we want. Everything else is piper's.
"""
import logging
import sys

import torch
from lightning.pytorch.callbacks import ModelCheckpoint
from piper.train.__main__ import VitsLightningCLI
from piper.train.vits.dataset import VitsDataModule
from piper.train.vits.lightning import VitsModel

logging.basicConfig(level=logging.INFO)
torch.backends.cuda.matmul.allow_tf32 = True
torch.backends.cudnn.allow_tf32 = True
torch.backends.cudnn.deterministic = False

sys.argv[0] = "piper.train"
VitsLightningCLI(
    VitsModel,
    VitsDataModule,
    trainer_defaults={
        "max_epochs": -1,
        "callbacks": [
            ModelCheckpoint(
                monitor="val_mel",
                mode="min",
                save_top_k=3,
                save_last=True,
                filename="epoch={epoch}-val_mel={val_mel:.4f}",
                auto_insert_metric_name=False,
            )
        ],
    },
)
