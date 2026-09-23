# QuirrelBasic

Dwukierunkowa synchronizacja zwykłych plików i folderów Windows z Google Drive, działająca cyklicznie jako usługa Windows lub program konsolowy. Konfiguracja odbywa się w JSON; aplikacja nie ma interfejsu graficznego.

## Reguły synchronizacji

- Nowszy czas modyfikacji pliku w UTC wygrywa. Identyczna suma MD5 oznacza brak transferu niezależnie od daty.
- Jeżeli treści różnią się przy identycznych datach, wygrywa komputer. Starsza/przegrywająca wersja jest zapisywana lokalnie w katalogu kopii zapasowych przed nadpisaniem.
- Brakujący plik jest kopiowany z istniejącej strony. Brakujące foldery i podfoldery są tworzone, również puste. Daty folderów nie rozstrzygają o zawartości: każdy plik jest porównywany osobno.
- Usunięcia nie są propagowane: usunięty plik zostanie odtworzony z drugiej strony. Zmiana nazwy jest traktowana jako nowy plik. Aby trwale usunąć plik, zatrzymaj usługę i usuń go po obu stronach.
- Jeżeli pojedynczego pliku nie ma nigdzie, program zgłasza to w logu; nie wymyśla jego zawartości.
- Pobieranie używa pliku tymczasowego, weryfikacji rozmiaru/MD5 i atomowej podmiany. Błąd sieci nie jest traktowany jako pusty folder. Nieudana para jest ponawiana w następnym cyklu; pozostałe pary nadal są przetwarzane.
- Powielone nazwy w Google Drive (również różniące się tylko wielkością liter), kolizje plik/folder, nazwy niedozwolone w Windows i dowiązania/junctions zatrzymują przetwarzanie danej pary z błędem.

## Szybki start

Wymagany Windows x64. Gotowy katalog wydania samodzielnego zawiera runtime; do budowania źródeł potrzebny jest SDK .NET 8.

1. W Google Cloud utwórz projekt i włącz **Google Drive API**.
2. Skonfiguruj ekran zgody OAuth (Google Auth Platform), dodaj własne konto jako użytkownika testowego, jeśli projekt jest w trybie Testing.
3. Utwórz klienta OAuth typu **Desktop app**, pobierz JSON i zapisz jako `client_secret.json` obok programu. To nie jest klucz konta serwisowego.
4. Skopiuj `drives_config.example.json` do `drives_config.json`. Przykład zawiera wskazane foldery Dark Souls III i Minecraft oraz docelowe miejsca `QuirrelBasic/...` na Twoim Dysku.
5. W PowerShell, w katalogu programu:

```powershell
.\QuirrelBasic.exe validate .\drives_config.json
.\QuirrelBasic.exe authorize .\drives_config.json
.\QuirrelBasic.exe dry-run .\drives_config.json
.\QuirrelBasic.exe once .\drives_config.json
```

`authorize` otwiera logowanie Google i zapisuje token odświeżania; pozostałe komendy nigdy nie otwierają logowania. `dry-run` pokazuje plan bez zmiany synchronizowanych plików/folderów; zapisuje logi i może odświeżyć token. `once` wykonuje jeden cykl. `run` pracuje aż do Ctrl+C lub zatrzymania usługi. Nie uruchamiaj dwóch procesów z tym samym katalogiem danych: plik blokady uniemożliwia równoległy start.

OAuth używa zakresu `https://www.googleapis.com/auth/drive`, ponieważ aplikacja musi widzieć również wcześniej istniejące, dowolnie wskazane pliki i foldery. Kod wykonuje operacje tylko w skonfigurowanych parach. Token i plik klienta przechowuj w katalogu dostępnym tylko dla swojego konta i administratorów. Nie umieszczaj ich w synchronizowanych katalogach ani w Git.

Dla zewnętrznej aplikacji OAuth w statusie Testing token odświeżania przy tym zakresie zwykle wygasa po 7 dniach. Do stałej pracy skonfiguruj właściwy status publikacji aplikacji w Google Cloud; po cofnięciu dostępu uruchom ponownie `authorize`. Nie wysyłaj nikomu tokenów ani haseł.

## Konfiguracja

Ścieżki względne są liczone od katalogu pliku konfiguracji, również gdy usługę uruchamia Windows. Zmienne środowiskowe w stylu `%APPDATA%` są rozwijane dla konta uruchamiającego program; dla usługi najlepiej stosować ścieżki absolutne. Zmiana konfiguracji wymaga restartu.

| Pole | Znaczenie |
| --- | --- |
| `GoogleClientSecretPath` | Plik JSON klienta OAuth Desktop |
| `DataDirectory` | Token, blokada pojedynczej instancji, logi i kopie; poza synchronizacją |
| `IntervalSeconds` | Przerwa po zakończeniu cyklu, minimum 5 s |
| `SettleSeconds` | Minimalny wiek ostatniej modyfikacji przed transferem |
| `Pairs[].LocalPath` | Lokalny plik lub folder |
| `Pairs[].GoogleRootId` | `root` dla Mojego dysku albo ID istniejącego folderu z adresu Drive |
| `Pairs[].RemotePath` | Ścieżka względem GoogleRootId, oddzielana znakiem `/`; tworzona automatycznie |
| `Pairs[].Kind` | `Folder` albo `File` |
| `Pairs[].PauseWhileProcessesRunning` | Nazwy procesów bez `.exe`, w czasie których para jest pomijana |

Przykład pojedynczego pliku:

```json
{
  "LocalPath": "C:\\Games\\example\\save.dat",
  "GoogleRootId": "root",
  "RemotePath": "QuirrelBasic/example/cloud-save.dat",
  "Kind": "File",
  "PauseWhileProcessesRunning": ["example"]
}
```

Nieistniejące ID folderu GoogleRootId nie może zostać odtworzone pod tym samym ID: wybierz `root` lub istniejący folder i wpisz brakującą ścieżkę w RemotePath. Par nie należy nakładać na siebie, również przez dwa różne identyfikatory wskazujące przodka/potomka na Drive.

## Usługa Windows

Najpierw wykonaj autoryzację i próbę synchronizacji interaktywnie. Umieść program w stałym katalogu, do którego wybrane konto ma dostęp. Następnie uruchom PowerShell jako administrator:

```powershell
$account = Get-Credential  # konto Windows z hasłem, nie PIN-em
.\scripts\Install-Service.ps1 -Executable "C:\\Apps\\QuirrelBasic\\QuirrelBasic.exe" -Config "C:\\Apps\\QuirrelBasic\\drives_config.json" -Credential $account
Start-Service QuirrelBasic
Get-Service QuirrelBasic
```

Użyj konta Windows, które ma dostęp do save'ów i wykonało OAuth. Konto musi mieć prawo „Log on as a service” (Logowanie jako usługa); w razie błędu logowania nadaj je w lokalnych zasadach zabezpieczeń. Instalator ustawia start automatyczny i odzyskiwanie po awarii. Nie uruchamia synchronizacji samodzielnie.

```powershell
Stop-Service QuirrelBasic
Restart-Service QuirrelBasic
.\scripts\Uninstall-Service.ps1
```

Deinstalacja zachowuje konfigurację i dane. Alternatywnie `QuirrelBasic.exe run` działa w konsoli bez instalowania usługi.

## Uruchamianie ręczne i autostart po zalogowaniu

W opublikowanym pakiecie można uruchomić `scripts/Start-Quirrel.cmd`. Domyślnie korzysta z `drives_config.json` obok programu. Opcjonalny pierwszy argument wskazuje inną konfigurację. Okno konsoli pozostaje otwarte; Ctrl+C zatrzymuje proces.

`scripts/Start-Quirrel.ps1` uruchamia aplikację bez widocznego okna, a `scripts/Stop-Quirrel.ps1` zatrzymuje procesy uruchomione z tego konkretnego katalogu programu. Zatrzymanie przez skrypt Stop jest wymuszone: przerwany transfer zostanie ponowiony po uruchomieniu. Skrypty PowerShell wymagają polityki wykonywania, która pozwala je uruchamiać. Skrypt CMD uruchamia program bezpośrednio. Dla zainstalowanej usługi używaj poleceń Start-Service i Stop-Service.

Jeżeli aplikacja ma działać dopiero po zalogowaniu do Windows, wystarczy zadanie w Harmonogramie zadań. Nie instaluj równocześnie usługi uruchamiającej tę samą konfigurację.

1. Utwórz zadanie z wyzwalaczem **Przy logowaniu** na Twoje konto.
2. W akcji **Uruchom program** podaj:
   - Program: pełna ścieżka do `QuirrelBasic.exe`.
   - Argumenty: `run "C:\\Apps\\QuirrelBasic\\drives_config.json"` (wstaw własną ścieżkę).
   - Rozpocznij w: katalog zawierający EXE, bez cudzysłowów.
3. Wybierz **Uruchom tylko wtedy, gdy użytkownik jest zalogowany**, bez najwyższych uprawnień.
4. Wyłącz limit **Zatrzymaj zadanie, jeśli działa dłużej niż...**. Dla kolejnego wystąpienia wybierz **Nie uruchamiaj nowego wystąpienia**.
5. Na laptopie dostosuj warunki zasilania, jeśli synchronizacja ma działać również na baterii.

Takie zadanie używa uprawnień zalogowanego użytkownika i nie wymaga zapisywania jego hasła. Bezpośrednie uruchomienie EXE może wyświetlić konsolę; opcja **Ukryte** w Harmonogramie nie ukrywa okna programu. Skrypt Start-Quirrel.ps1 uruchamia odłączony proces, więc Harmonogram nie śledzi wtedy czasu działania samego synchronizatora.

Po zmianie listy Pairs zatrzymaj i ponownie uruchom program. Nie przenoś ani nie usuwaj wskazanej konfiguracji, katalogu danych ani pliku klienta OAuth.

## Save'y, kopie i ograniczenia

Przykład wstrzymuje Dark Souls III podczas procesu `DarkSoulsIII`, a Minecraft podczas `java` lub `javaw` (dotyczy też innych programów Java). Zamknij grę i zaczekaj na zakończenie synchronizacji przed uruchomieniem jej na drugim komputerze. Synchronizacja jest wykonywana plik po pliku, nie stanowi transakcji całego świata Minecraft.

Kopie są w `data/backups/YYYY-MM-DD`, opisane identyfikatorem i nazwą pliku. Towarzyszący JSON wskazuje oryginalną ścieżkę i stronę pochodzenia. Aby przywrócić kopię: zatrzymaj usługę, skopiuj wybrany plik na miejsce, ustaw aktualny czas modyfikacji, a następnie uruchom synchronizację. Niepełna kopia może pozostać po błędzie pobierania; oryginał w takim przypadku nie jest nadpisywany. Logi są w `data/logs`. Kopie i logi nie są automatycznie usuwane — kontroluj zajęte miejsce.

Nie edytuj równocześnie tych samych plików na dwóch komputerach. Kontrola metadanych wykrywa zmiany podczas pobierania i przed wysyłaniem, ale nie zapewnia rozproszonej blokady ani atomowego warunku zapisu na Google Drive. Między ostatnią kontrolą a zapisem nadal może nastąpić równoległa zmiana. Lokalne kopie przechowują wersję odczytaną przed nadpisaniem. Zegary komputerów powinny być zsynchronizowane.

Obsługiwane są zwykłe pliki binarne na Moim dysku. Dokumenty Google Docs/Sheets/Slides, skróty Drive, dyski współdzielone i dowiązania Windows nie są obsługiwane. Prefiks `.quirrel-` jest zarezerwowany dla plików roboczych. Plik `.quirrel-*.recovery` to poprzednia wersja pliku zachowana przy atomowej podmianie: jeżeli pozostanie po awarii, zachowaj go do odzyskania danych. Pozostałości `.tmp` można usunąć po zatrzymaniu usługi.

## Budowanie i testy

```powershell
dotnet build QuirrelBasic.csproj -c Release
dotnet run --project tests/QuirrelBasic.Tests.csproj -c Release
dotnet publish QuirrelBasic.csproj -c Release -r win-x64 --self-contained true -o publish
Copy-Item drives_config.example.json publish/
Copy-Item scripts publish/ -Recurse
```

Testy to samodzielny program zwracający niezerowy kod w przypadku błędu. Używają sztucznego Drive i tymczasowych plików, nie wymagają konta Google. Workflow GitHub Actions kompiluje program, wykonuje testy i buduje wydanie Windows x64.

Stan weryfikacji tego wydania: kompilacja została wykonana lokalnie. Uruchomienie testów na komputerze wykonawczym zostało zablokowane przez Windows Application Control (0x800711C7); nie jest to potwierdzenie zaliczenia testów. Połączenie z prawdziwym Google Drive i instalacja w Service Control Manager wymagają jeszcze testu po autoryzacji użytkownika.

Dokumentacja API: [Google Drive uploads](https://developers.google.com/workspace/drive/api/guides/manage-uploads), [metadane plików](https://developers.google.com/workspace/drive/api/reference/rest/v3/files), [usługi Windows w .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service), [ważność tokenów OAuth](https://developers.google.com/identity/protocols/oauth2#expiration).
