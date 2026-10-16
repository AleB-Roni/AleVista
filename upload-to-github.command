#!/bin/bash
# AleVista → GitHub hochladen (Doppelklick)
cd "$(dirname "$0")" || exit 1

REPO_OWNER="AleB-Roni"
REPO_NAME="AleVista"
REMOTE_URL="https://github.com/$REPO_OWNER/$REPO_NAME.git"

echo "=== AleVista → GitHub ==="
echo

if ! command -v git >/dev/null 2>&1; then
  echo "Git ist nicht installiert. Einmal im Terminal ausführen:  xcode-select --install"
  read -n 1 -s -r -p "Taste drücken zum Schließen …"; exit 1
fi

if [ ! -d .git ]; then
  git init -q -b main
  echo "Neues Git-Repository angelegt."
fi

if ! git remote get-url origin >/dev/null 2>&1; then
  if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
    echo "Erstelle privates Repo $REPO_OWNER/$REPO_NAME …"
    gh repo create "$REPO_OWNER/$REPO_NAME" --private --source . --remote origin >/dev/null 2>&1 \
      || git remote add origin "$REMOTE_URL"
  else
    git remote add origin "$REMOTE_URL"
    echo "Hinweis: Lege das Repo $REPO_OWNER/$REPO_NAME auf github.com an, falls es noch nicht existiert."
  fi
fi

git add -A
if git diff --cached --quiet; then
  echo "Keine Änderungen zum Hochladen."
else
  git commit -q -m "Update $(date '+%Y-%m-%d %H:%M')"
  echo "Änderungen committet."
fi

echo "Lade hoch …"
if git push -u origin main; then
  echo
  echo "✅ Hochgeladen. GitHub baut jetzt die AleVista.exe (Reiter „Actions“)."
else
  echo
  echo "❌ Push fehlgeschlagen – existiert das Repo und bist du angemeldet?"
  read -n 1 -s -r -p "Taste drücken zum Schließen …"; exit 1
fi

echo
read -r -p "Als Release veröffentlichen? Version eingeben (z. B. v1.0.0) oder Enter zum Überspringen: " VERSION
if [ -n "$VERSION" ]; then
  git tag -a "$VERSION" -m "AleVista $VERSION" && git push origin "$VERSION" \
    && echo "✅ Release $VERSION wird gebaut – die .exe erscheint gleich unter „Releases“."
fi

echo
read -n 1 -s -r -p "Fertig. Taste drücken zum Schließen …"
