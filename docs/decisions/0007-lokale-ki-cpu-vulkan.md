# 0007: Lokale Sprachmodelle auf CPU oder Vulkan-GPU

Stand: gilt, 2026-11-08

## Kontext

Granite und Qwen sollen auf einer T600 mit 4 GiB VRAM beschleunigt werden, waehrend Suche und Einordnung parallel die CPU nutzen. Zusaetzliche CUDA-Redistributables bringen NVIDIA-Lizenzbedingungen mit. Gewuenscht ist weiterhin kommerziell weitergebbare Software ohne neue gebuehrenpflichtige Laufzeit.

## Entscheidung

- CPU bleibt immer verfuegbar. Optional wird der offizielle Windows-Vulkan-Build derselben gepinnten llama.cpp-Version b12110 mit Groessen-/SHA256-Pruefung nachgeladen. Die bestehende MIT-Lizenz und LLVM/OpenMP-Hinweise gelten weiterhin; keine CUDA-Bibliotheken oder Grafiktreiber werden mitgeliefert. Nutzung des bereits installierten Treibers.
- Generierung und CPU-Einordnung haben getrennte Gates. ONNX bleibt CPU-basiert und begrenzt; CPU-Clustering laeuft ausserhalb des UI-Threads.
- Ein lokaler synthetischer Vergleich misst CPU, vollstaendige GPU-Auslagerung und hybride Schichtverteilung. Verschluesselte Messprofile gelten je Modell, Laufzeit, CPU, GPU und Treiber; die sichere schnellste Variante wird bevorzugt. Benutzer kann CPU erzwingen oder GPU bevorzugen.
- RAM-/Commit-/VRAM-Reserven, niedrige CPU-/GPU-Prioritaet, Hintergrundpausen, GPU-Temperaturgrenzen und Entladen nach Leerlauf begrenzen Ressourcenlast. Ausfall einer GPU-Anfrage fuehrt einmal zur CPU. Unbekannte Sensoren werden nicht als Messwerte ausgegeben.

## Folgen

- Kein zusaetzliches NuGet-Paket, keine Treiberinstallation. Vulkan ist eine bewusste Alternative zu CUDA; die GPU wird dennoch fuer Generierung genutzt.
- CPU-Pakettemperatur ist ohne weitere Sensorunterstuetzung nicht verlaesslich bekannt. Taktverhaeltnisse werden nur als Lastindikator verwendet. Fremde Lastspruenge und Paging sind nicht vollstaendig vermeidbar.
- Kurze synthetische Messungen liefern keine universelle Geschwindigkeitsgarantie; erneuter Vergleich ist in der Kontrollstation verfuegbar.

## Quellen

- https://github.com/ggml-org/llama.cpp/releases/tag/b12110
- https://github.com/ggml-org/llama.cpp/blob/b12110/LICENSE
- https://github.com/ggml-org/llama.cpp/blob/b12110/tools/server/README.md
- https://docs.nvidia.com/cuda/archive/12.4.0/eula/index.html
