# OCR 模型

使用 RapidAI 发布的 PaddleOCR PP-OCRv5 mobile 中英文 ONNX 模型；按下表改名，内容未修改。默认禁用了 RapidOcrNet NuGet 的拉丁文模型复制规则。

来源目录：
https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv5/

| 本地文件 | 来源相对路径 | SHA-256 |
| --- | --- | --- |
| detection.onnx | det/ch_PP-OCRv5_det_mobile.onnx | 4d97c44a20d30a81aad087d6a396b08f786c4635742afc391f6621f5c6ae78ae |
| recognition.onnx | rec/ch_PP-OCRv5_rec_mobile.onnx | 5825fc7ebf84ae7a412be049820b4d86d77620f204a041697b0494669b1742c5 |
| orientation.onnx | cls/ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx | 54379ae5174d026780215fc748a7f31910dee36818e63d49e17dc598ecc82df7 |
| dictionary.txt | 下方 PaddleOCR 字典 | d1979e9f794c464c0d2e0b70a7fe14dd978e9dc644c0e71f14158cdf8342af1b |

字典：https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/main/ppocr/utils/dict/ppocrv5_dict.txt

上游清单：https://github.com/RapidAI/RapidOCR/blob/main/python/rapidocr/default_models.yaml

模型及字典对应许可证见 `../Licenses/PaddleOCR.txt` 和 `../Licenses/RapidOCR.txt`。首次识别从插件程序集所在目录读取这些文件，不依赖启动目录或系统安装的 OCR 软件。
