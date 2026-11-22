# 🎙️ SimpleMicCompressor

<p align="center">
  <b>Language / Язык:</b>
  <br>
  <b>🇷🇺 Русский</b> &nbsp;|&nbsp;
  <a href="README.md">🇬🇧 English</a>
</p>

<p align="center">
  <img src="assets/screenshot_main_ru.png" alt="Интерфейс SimpleMicCompressor" width="850" />
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Author-Diesel--X-blue?style=flat-square&logo=github" alt="Author" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-informational?style=flat-square&logo=windows" alt="Platform" />
  <img src="https://img.shields.io/badge/Audio-WASAPI%20Shared%20Mode-success?style=flat-square" alt="Audio" />
  <img src="https://img.shields.io/badge/Latency-20--25%20ms-green?style=flat-square" alt="Latency" />
  <img src="https://img.shields.io/badge/License-MIT-orange?style=flat-square" alt="License" />
</p>

**SimpleMicCompressor** — системный процессор микрофона в реальном времени для **Discord**, **стримов** и **соревновательных игр** (CS2, Valorant, Apex Legends, Warzone). Выравнивает громкость речи, срезает клики и гул, защищает от клиппинга и не влияет на FPS.

---

## 📦 Версии программы

| | 🎨 **SimpleMicCompressor (GUI) — Рекомендуется** | ⚡ SimpleMicCompressor Light (Rust) *(Экспериментальная)* |
| :--- | :--- | :--- |
| **Файл** | `SimpleMicCompressor.exe` (C# / .NET 8 / WPF) | `SimpleMicCompressor_Light.exe` (Rust) |
| **Интерфейс** | Студийный UI, VU-метры, мастер настройки, трей | Только фоновый сервис в трее Windows |
| **Ресурсы** | ~15–25 МБ RAM, < 0.1% CPU | ~3–5 МБ RAM, 0.0% CPU |
| **Скачать** | [📥 **GitHub Releases (Рекомендуется)**](https://github.com/Diesel-x/SimpleMicCompressor/releases) | [⚡ **GitHub Releases**](https://github.com/Diesel-x/SimpleMicCompressor/releases) |

---

## 🚀 Быстрый старт

1. Скачайте **`SimpleMicCompressor.exe` (Рекомендуется)** со [страницы релизов](https://github.com/Diesel-x/SimpleMicCompressor/releases).
2. Запустите программу и пройдите **мастер быстрой настройки**:
   * Выберите ваш физический микрофон.
   * Установите **`SMC virtual cable`** в 1 клик (если ещё не установлен).
   * При необходимости выберите наушники для мониторинга.
3. В **Discord**, **Telegram** или **игре**:
   * Выберите устройство ввода: **`CABLE Output (VB-Audio Virtual Cable)`**.
   * Отключите встроенное шумоподавление Krisp / авторегулировку громкости (AGC) для чистого звучания.

---

## ⌨️ Глобальные горячие клавиши

Работают в полноэкранных играх без `Alt+Tab` (настраиваются в Настройках):

| Действие | По умолчанию | Описание |
| :--- | :--- | :--- |
| **Bypass** | `Ctrl + Shift + B` | Пропускает чистый сигнал микрофона без обработки |
| **Meme Mode** | `Ctrl + Shift + M` | Включает эффект «уничтоженного микрофона» |
| **Mute** | `Ctrl + Shift + X` | Мгновенное заглушение микрофона |

---

## 🛠️ Цепочка обработки (DSP)

* **Low-Cut 80 Hz** — фильтр среза низких частот (убирает удары по столу и гул вентиляторов).
* **Noise Gate** — шумоподавитель с удержанием 60 мс и гистерезисом.
* **Dynamic Compressor** — выравнивает тихий шёпот и громкие пики.
* **Fast Limiter (-1.0 dBFS)** — пик-лимитер с мягким насыщением против хрипа и клиппинга.
* **Meme Mode** — режим перегруза микрофона для фана в Discord.

---

## 📄 Лицензия и автор

* **Автор:** [Diesel-X](https://github.com/Diesel-x)
* **Лицензия:** [MIT License](LICENSE)
