# SAME model attribution

The deep-learning retarget mode in this folder is built on **SAME: Skeleton-Agnostic
Motion Embedding for Character Animation** by Sunmin Lee, Taeho Kang, Jungnam Park,
Jehee Lee and Jungdam Won (SIGGRAPH Asia 2023 Conference Papers).

- Project: https://github.com/sunny-Codes/SAME
- Paper: https://doi.org/10.1145/3610548.3618206

`same_v1.weights` contains the network parameters and feature-normalization statistics
**derived from the authors' pretrained checkpoint** (`result/ckpt0/last_model.pt` +
`ms_dict.pt` at commit `61fac8a`), converted to a plain float32 tensor blob for this
library's pure-managed inference port (no architecture or weight changes — see
`dev/m10/scripts/export_weights.py`).

## License

The SAME model and its pretrained weights are licensed **CC BY-NC 4.0**
(Creative Commons Attribution-NonCommercial 4.0 International,
https://creativecommons.org/licenses/by-nc/4.0/).

- Attribution required (this notice must accompany the weight asset).
- **Non-commercial use only.** Projects using the deep-learning retarget mode
  commercially must remove `same_v1.weights` or obtain a separate license from the
  authors. The rest of the humanoid-retargeter library (including the geometric solver)
  does not depend on this asset and is unaffected.
