"""Several detectors as one (S53): one ONNX whose heat maps are the mean of the members' sigmoids, given back
as logits (the reader takes the sigmoid of what it reads), so the reader runs it as any other model. Models
learnt alike but for chance differ on the real scans by 150-200 notes, each losing other pages (detector49,
42's making with another seed: chopin28-4 scan -82, dl scan -50); a mean of them should lose fewer.
detector42 + 49 (2026-11-02): the evaluation set's notes 24 under 42 (49 alone 182 under; chopin28-4 scan
+5, dl scan still -36), the development set 17 under, a page read in 1.3 times as long.

  python ensemble.py <out.onnx> <a.pt> <b.pt> [<c.pt> ...]
"""
import sys
import torch, torch.nn as nn
from dataset import HEIGHT
from train import Detector


class Mean(nn.Module):
    def __init__(self, nets):
        super().__init__()
        self.nets = nn.ModuleList(nets)

    def forward(self, x):
        p = torch.stack([torch.sigmoid(n(x)) for n in self.nets]).mean(0).clamp(1e-6, 1 - 1e-6)
        return torch.log(p / (1 - p))


if __name__ == '__main__':
    out, models = sys.argv[1], sys.argv[2:]
    nets = []
    for m in models:
        n = Detector(); n.load_state_dict(torch.load(m, map_location='cpu')); n.eval(); nets.append(n)
    net = Mean(nets).eval()
    x = torch.zeros(1, 1, HEIGHT, 512)
    torch.onnx.export(net, x, out, input_names=['strip'], output_names=['heat'], dynamic_axes={'strip': {3: 'width'}, 'heat': {3: 'width4'}}, opset_version=17, dynamo=False)
    print('->', out)
