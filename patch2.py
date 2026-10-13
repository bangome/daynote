import re

with open('src/Daynote.Desktop/Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('Foreground="Transparent"', 'Foreground="#01000000"')

with open('src/Daynote.Desktop/Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
