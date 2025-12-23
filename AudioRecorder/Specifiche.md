# Specifiche del Registratore Audio
- Realizzato in C# e .NET 10
- registrare audio dal microfono
- registrare audio di sistema
- mixare i due flussi audio in tempo reale
- sincronizzare le due registrazioni
- salvare la registrazione in un file WAV

## Implementazione
- **NAudio**: Libreria per gestione audio
- **WaveInEvent**: Cattura audio microfono con selezione dispositivo
- **WasapiLoopbackCapture**: Cattura audio sistema con conversione formato avanzata
- **Resampling**: WdlResamplingSampleProvider per conversione sample rate
- **Conversione canali**: Supporto mono->stereo e multi-channel->stereo
- **Sincronizzazione**: Timestamp UTC per allineare i flussi
- **Mixing**: Combinazione dei due canali audio durante il salvataggio
- **Output**: File WAV 44.1kHz, 16-bit, stereo

## Tecnologie Front-End
- **Windows Presentation Foundation (WPF)**: Framework UI moderno per applicazioni desktop Windows
- **XAML**: Markup per definire l'interfaccia utente
- **Data Binding**: Pattern MVVM-ready per future estensioni
- **Dispatcher**: Threading model WPF per aggiornamenti UI

## Funzionalità
- Interfaccia WPF moderna con styling
- **Selezione dispositivo di input** tramite ComboBox
- Controlli Start/Stop/Save con feedback visivo
- Indicatore di stato in tempo reale
- Gestione errori con MessageBox
- Registrazione su file separati e mixing durante il salvataggio
- Salvataggio file con nome automatico basato su timestamp
- **Risoluzione errore BadDeviceId** tramite selezione esplicita del dispositivo
- **Correzione rumore bianco** tramite conversione formato appropriata

## Architettura
- **Pattern**: Code-behind con event handling
- **Layout**: Grid layout responsivo
- **Styling**: Colori e dimensioni inline per chiarezza
- **Threading**: Dispatcher.Invoke per thread safety
- **Gestione dispositivi**: Servizio dedicato per enumerazione e selezione dispositivi audio
- **Conversione audio**: Pipeline di conversione con resampling e gestione canali

## Correzioni Applicate
- **Rumore gaussiano bianco**: Risolto tramite conversione formato audio corretta
- **Compatibilità formati**: Gestione automatica di sample rate e numero di canali diversi
- **Gestione errori**: Migliore handling degli errori di conversione audio
