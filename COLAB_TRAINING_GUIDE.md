# ♻️ Step-by-Step Guide: Training YOLOv8 Waste Model in Google Colab

This guide walks you through training the custom **YOLOv8** waste detection model for the **AI Smart Bin** using a free cloud GPU on **Google Colab** in ~10–12 minutes.

---

## 📌 Step 1. Open the Notebook in Google Colab

1. Open your browser and navigate to:  
   👉 [https://colab.research.google.com/](https://colab.research.google.com/)
2. In the dialog, select the **Upload** tab.
3. Click **Browse** and select the notebook from the repository:  
   `Train_SmartBin_YOLO.ipynb`

---

## ⚡ Step 2. Enable Free Cloud GPU

1. In the top Colab menu, click:  
   **Runtime** ➔ **Change runtime type**.
2. Under Hardware accelerator, select: **T4 GPU** (free tier).
3. Click **Save**.

---

## 🚀 Step 3. Run Training (1-Click)

1. In the top menu, select:  
   **Runtime** ➔ **Run all** (`Ctrl + F9`).
2. The notebook will automatically:
   * Verify GPU acceleration;
   * Install `ultralytics` and `onnx` dependencies;
   * Download the annotated waste classification dataset (plastic, paper, organic, glass, metal);
   * Train `yolov8n` for 30 epochs (~10–12 minutes);
   * Export the trained weights to `.onnx` format;
   * Automatically trigger download of `best.onnx` directly to your browser!

---

## 📂 Step 4. Add `best.onnx` to the Project

Once `best.onnx` finishes downloading:
1. Copy `best.onnx` into the `SmartBinApp/` folder (or next to `SmartBinApp.exe`).

---

## 🎮 Step 5. Run SmartBinApp

1. Launch the application in Visual Studio or via CLI (`dotnet run`).
2. The AI status badge in the top right will display: **`✓ best.onnx`**!
3. Connect your **Arduino** board (or turn on **Demo Mode**).
4. Click **Start Camera**.
5. Ensure **Automatic Sorting** is checked.
6. Present items to the camera — the AI will detect the category, display the bounding box/HUD, and actuate the corresponding bin lid!
