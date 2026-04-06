#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <vector>

#if defined(__APPLE__)
#include <AudioToolbox/AudioComponent.h>
#include <CoreFoundation/CoreFoundation.h>
#endif

#include <pluginterfaces/base/funknown.h>
#include <pluginterfaces/vst/ivstaudioprocessor.h>
#include <public.sdk/source/vst/moduleinfo/moduleinfoparser.h>

#include <juce_audio_processors/juce_audio_processors.h>
#include <juce_gui_basics/juce_gui_basics.h>

namespace {
constexpr int kDefaultChannelCount = 2;

void writeStringToBuffer(const juce::String& text, char* buffer,
                         int bufferBytes) {
    if (buffer == nullptr || bufferBytes <= 0) {
        return;
    }

    auto utf8 = text.toRawUTF8();
    auto length =
        std::min<int>(bufferBytes - 1, static_cast<int>(std::strlen(utf8)));
    std::memcpy(buffer, utf8, static_cast<size_t>(length));
    buffer[length] = '\0';
}

juce::String toJuceString(const std::string& text) {
    return juce::String::fromUTF8(text.c_str());
}

template <typename Range> int getHashForByteRange(Range&& range) noexcept {
    juce::uint32 value = 0;

    for (const auto& item : range) {
        value = (value * 31u) +
                static_cast<juce::uint32>(static_cast<unsigned char>(item));
    }

    return static_cast<int>(value);
}

template <typename Range> int getHashForRange(Range&& range) noexcept {
    juce::uint32 value = 0;

    for (const auto& item : range) {
        value = (value * 31u) + static_cast<juce::uint32>(item);
    }

    return static_cast<int>(value);
}

bool tryLoadVst3ModuleInfoDescription(const juce::String& fileOrIdentifier,
                                      juce::PluginDescription& result) {
    auto file = juce::File::createFileWithoutCheckingPath(fileOrIdentifier);
    if (!file.hasFileExtension(".vst3") || !file.exists()) {
        return false;
    }

    auto moduleInfoFile = file.getChildFile("Contents")
                              .getChildFile("Resources")
                              .getChildFile("moduleinfo.json");
    if (!moduleInfoFile.existsAsFile()) {
        moduleInfoFile =
            file.getChildFile("Contents").getChildFile("moduleinfo.json");
    }

    if (!moduleInfoFile.existsAsFile()) {
        return false;
    }

    juce::MemoryBlock data;
    if (!moduleInfoFile.loadFileAsData(data) || data.getSize() == 0) {
        return false;
    }

    std::ostringstream parseErrors;
    auto parsed = Steinberg::ModuleInfoLib::parseJson(
        std::string_view(static_cast<const char*>(data.getData()),
                         data.getSize()),
        &parseErrors);

    if (!parsed.has_value()) {
        return false;
    }

    for (const auto& pluginClass : parsed->classes) {
        if (pluginClass.category != kVstAudioEffectClass) {
            continue;
        }

        Steinberg::FUID fuid;
        if (!fuid.fromString(pluginClass.cid.c_str())) {
            continue;
        }

        juce::PluginDescription description;
        description.fileOrIdentifier = file.getFullPathName();
        description.lastFileModTime = file.getLastModificationTime();
        description.lastInfoUpdateTime = juce::Time::getCurrentTime();
        description.manufacturerName =
            toJuceString(pluginClass.vendor.empty() ? parsed->factoryInfo.vendor
                                                    : pluginClass.vendor);
        description.name = toJuceString(pluginClass.name);
        description.descriptiveName = description.name;
        description.pluginFormatName = "VST3";
        description.numInputChannels = 0;
        description.numOutputChannels = 0;
        description.version = toJuceString(pluginClass.version);

        juce::StringArray categories;
        bool isInstrument = false;

        for (const auto& subCategory : pluginClass.subCategories) {
            auto category = toJuceString(subCategory);
            categories.add(category);

            if (category.equalsIgnoreCase("Instrument")) {
                isInstrument = true;
            }
        }

        description.category = categories.joinIntoString("|");
        description.isInstrument = isInstrument;

        const auto& rawTuid = fuid.toTUID();
        description.deprecatedUid = getHashForByteRange(rawTuid);
        description.uniqueId = getHashForRange(
            std::array<juce::uint32, 4>{fuid.getLong1(), fuid.getLong2(),
                                        fuid.getLong3(), fuid.getLong4()});

        result = std::move(description);
        return true;
    }

    return false;
}

bool isAudioUnitBundlePath(const juce::String& fileOrIdentifier) {
#if JUCE_MAC
    auto file = juce::File::createFileWithoutCheckingPath(fileOrIdentifier);
    return (file.hasFileExtension(".component") ||
            file.hasFileExtension(".appex")) &&
           file.isDirectory();
#else
    juce::ignoreUnused(fileOrIdentifier);
    return false;
#endif
}

#if JUCE_MAC
bool tryConvertFourCCText(const juce::String& text, OSType& value) {
    if (text.length() != 4) {
        return false;
    }

    value = 0;

    for (int index = 0; index < 4; index++) {
        auto character = text[index];
        if (character < 0 || character > 0xff) {
            return false;
        }

        value =
            static_cast<OSType>((value << 8) | static_cast<OSType>(character));
    }

    return true;
}

bool tryReadOstype(CFTypeRef valueRef, OSType& value) {
    if (valueRef == nullptr) {
        return false;
    }

    if (CFGetTypeID(valueRef) == CFStringGetTypeID()) {
        return tryConvertFourCCText(
            juce::String::fromCFString(static_cast<CFStringRef>(valueRef)),
            value);
    }

    if (CFGetTypeID(valueRef) == CFNumberGetTypeID()) {
        UInt32 numericValue = 0;
        if (CFNumberGetValue(static_cast<CFNumberRef>(valueRef),
                             kCFNumberSInt32Type, &numericValue)) {
            value = numericValue;
            return true;
        }
    }

    return false;
}

UInt32 readUInt32Property(CFTypeRef valueRef, UInt32 defaultValue = 0) {
    if (valueRef == nullptr) {
        return defaultValue;
    }

    if (CFGetTypeID(valueRef) == CFNumberGetTypeID()) {
        UInt32 numericValue = defaultValue;
        if (CFNumberGetValue(static_cast<CFNumberRef>(valueRef),
                             kCFNumberSInt32Type, &numericValue)) {
            return numericValue;
        }
    }

    if (CFGetTypeID(valueRef) == CFStringGetTypeID()) {
        return static_cast<UInt32>(
            juce::String::fromCFString(static_cast<CFStringRef>(valueRef))
                .getLargeIntValue());
    }

    return defaultValue;
}

CFStringRef readStringProperty(CFDictionaryRef dictionary, CFStringRef key) {
    auto valueRef = CFDictionaryGetValue(dictionary, key);
    return valueRef != nullptr && CFGetTypeID(valueRef) == CFStringGetTypeID()
               ? static_cast<CFStringRef>(valueRef)
               : nullptr;
}
#endif

void pumpMessageThreadWhileWaiting() {
#if JUCE_MAC
    CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.01, true);
#else
    juce::Thread::sleep(10);
#endif
}

class JuceRuntime final {
  public:
    JuceRuntime() = default;

  private:
    juce::ScopedJuceInitialiser_GUI initialiser;
};

JuceRuntime& getJuceRuntime() {
    static auto* runtime = new JuceRuntime();
    return *runtime;
}

class PluginEditorWindow final : public juce::DocumentWindow {
  public:
    explicit PluginEditorWindow(juce::AudioProcessor& processor,
                                const juce::String& title)
        : juce::DocumentWindow(
              title,
              juce::LookAndFeel::getDefaultLookAndFeel().findColour(
                  juce::ResizableWindow::backgroundColourId),
              juce::DocumentWindow::minimiseButton |
                  juce::DocumentWindow::closeButton),
          _constrainer(*this) {
        setUsingNativeTitleBar(true);
        setSize(400, 300);

        if (auto* editor = processor.createEditorIfNeeded()) {
            setContentOwned(editor, true);
            setResizable(editor->isResizable(), false);
        }

        setConstrainer(&_constrainer);
        setTopLeftPosition(120, 120);
        setVisible(true);
    }

    ~PluginEditorWindow() override { clearContentComponent(); }

    bool hasEditorContent() const { return getContentComponent() != nullptr; }

    void reveal() {
        setVisible(true);
        toFront(true);
    }

    void dismiss() {
        setVisible(false);
        clearContentComponent();
        removeFromDesktop();
    }

    void closeButtonPressed() override { setVisible(false); }

  private:
    class DecoratorConstrainer final
        : public juce::BorderedComponentBoundsConstrainer {
      public:
        explicit DecoratorConstrainer(juce::DocumentWindow& window)
            : _window(window) {}

        juce::ComponentBoundsConstrainer*
        getWrappedConstrainer() const override {
            auto* editor = dynamic_cast<juce::AudioProcessorEditor*>(
                _window.getContentComponent());
            return editor != nullptr ? editor->getConstrainer() : nullptr;
        }

        juce::BorderSize<int> getAdditionalBorder() const override {
            const auto nativeFrame = [&]() -> juce::BorderSize<int> {
                if (auto* peer = _window.getPeer()) {
                    if (const auto frameSize = peer->getFrameSizeIfPresent()) {
                        return *frameSize;
                    }
                }

                return {};
            }();

            return nativeFrame.addedTo(_window.getContentComponentBorder());
        }

      private:
        juce::DocumentWindow& _window;
    };

    float getDesktopScaleFactor() const override { return 1.0f; }

    DecoratorConstrainer _constrainer;
};

class PluginHost final {
  public:
    PluginHost() {
        getJuceRuntime();
        juce::addDefaultFormatsToManager(_formatManager);
    }

    ~PluginHost() { unload(); }

    bool loadPlugin(const std::string& path, double sampleRate,
                    int maximumBlockSize, int channels, juce::String& error) {
        auto loadOnMessageThread = [&]() -> juce::Result {
            juce::PluginDescription description;
            if (!findPluginDescription(toJuceString(path), description,
                                       error)) {
                return juce::Result::fail(error);
            }

            if (!registerAudioUnitBundleIfNeeded(description.fileOrIdentifier,
                                                 error)) {
                return juce::Result::fail(error);
            }

            juce::String createError;
            auto instance = createPluginInstanceOnMessageThread(
                description, sampleRate, maximumBlockSize, createError);

            if (instance == nullptr) {
                auto message =
                    createError.isNotEmpty()
                        ? createError
                        : juce::String(
                              "Failed to instantiate the selected plug-in.");
                return juce::Result::fail(message);
            }

            if (!configureStereoEffect(*instance, channels, error)) {
                return juce::Result::fail(error);
            }

            instance->setNonRealtime(false);
            instance->setRateAndBufferSizeDetails(sampleRate, maximumBlockSize);
            instance->prepareToPlay(sampleRate, maximumBlockSize);

            std::scoped_lock lock(_mutex);
            closeEditorWindowLocked();
            releaseResourcesLocked();

            _plugin = std::move(instance);
            _pluginPath = description.fileOrIdentifier;
            _pluginName = _plugin->getName();
            _sampleRate = sampleRate;
            _maximumBlockSize = std::max(1, maximumBlockSize);
            _channelCount = std::max(1, channels);
            _prepared = true;
            ensureAudioBufferCapacity(_maximumBlockSize, _channelCount);
            return juce::Result::ok();
        };

        const auto result =
            juce::MessageManager::getInstance()->isThisTheMessageThread()
                ? loadOnMessageThread()
                : juce::MessageManager::callSync(loadOnMessageThread)
                      .value_or(juce::Result::fail(
                          "Could not dispatch plug-in loading to the JUCE "
                          "message thread."));

        if (result.wasOk()) {
            return true;
        }

        error = result.getErrorMessage();
        return false;
    }

    bool prepare(double sampleRate, int maximumBlockSize, int channels,
                 juce::String& error) {
        auto prepareOnMessageThread = [&]() -> juce::Result {
            std::scoped_lock lock(_mutex);

            if (_plugin == nullptr) {
                return juce::Result::ok();
            }

            if (!configureStereoEffect(*_plugin, channels, error)) {
                return juce::Result::fail(error);
            }

            if (_prepared) {
                _plugin->releaseResources();
            }

            _plugin->setNonRealtime(false);
            _plugin->setRateAndBufferSizeDetails(sampleRate, maximumBlockSize);
            _plugin->prepareToPlay(sampleRate, maximumBlockSize);

            _sampleRate = sampleRate;
            _maximumBlockSize = std::max(1, maximumBlockSize);
            _channelCount = std::max(1, channels);
            _prepared = true;
            ensureAudioBufferCapacity(_maximumBlockSize, _channelCount);
            return juce::Result::ok();
        };

        const auto result =
            juce::MessageManager::getInstance()->isThisTheMessageThread()
                ? prepareOnMessageThread()
                : juce::MessageManager::callSync(prepareOnMessageThread)
                      .value_or(juce::Result::fail(
                          "Could not dispatch plug-in preparation to the JUCE "
                          "message thread."));

        if (result.wasOk()) {
            return true;
        }

        error = result.getErrorMessage();
        return false;
    }

    void unload() {
        auto unloadOnMessageThread = [&]() {
            std::scoped_lock lock(_mutex);
            closeEditorWindowLocked();
            releaseResourcesLocked();
            _plugin.reset();
            _pluginPath.clear();
            _pluginName.clear();
            _prepared = false;
            _audioBuffer.setSize(0, 0, false, false, true);
            return true;
        };

        if (juce::MessageManager::getInstance()->isThisTheMessageThread()) {
            unloadOnMessageThread();
            return;
        }

        juce::MessageManager::callSync(unloadOnMessageThread);
    }

    bool hasPlugin() const {
        std::scoped_lock lock(_mutex);
        return _plugin != nullptr;
    }

    bool hasEditor() const {
        std::scoped_lock lock(_mutex);
        return _plugin != nullptr && _plugin->hasEditor();
    }

    juce::String getPluginName() const {
        std::scoped_lock lock(_mutex);
        return _pluginName;
    }

    std::vector<std::uint8_t> getState(juce::String& error) {
        std::vector<std::uint8_t> stateBytes;

        auto getStateOnMessageThread = [&]() -> juce::Result {
            std::scoped_lock lock(_mutex);

            if (_plugin == nullptr) {
                return juce::Result::fail("No plug-in is loaded.");
            }

            juce::MemoryBlock state;
            _plugin->getStateInformation(state);

            stateBytes.resize(state.getSize());
            if (!stateBytes.empty()) {
                std::memcpy(stateBytes.data(), state.getData(),
                            state.getSize());
            }

            return juce::Result::ok();
        };

        const auto result =
            juce::MessageManager::getInstance()->isThisTheMessageThread()
                ? getStateOnMessageThread()
                : juce::MessageManager::callSync(getStateOnMessageThread)
                      .value_or(juce::Result::fail(
                          "Could not dispatch plug-in state export to the JUCE "
                          "message thread."));

        if (result.wasOk()) {
            return stateBytes;
        }

        error = result.getErrorMessage();
        return {};
    }

    bool setState(const void* stateData, int stateBytes, juce::String& error) {
        auto setStateOnMessageThread = [&]() -> juce::Result {
            std::scoped_lock lock(_mutex);

            if (_plugin == nullptr) {
                return juce::Result::fail("No plug-in is loaded.");
            }

            if (stateData == nullptr || stateBytes < 0) {
                return juce::Result::fail("Invalid plug-in state data.");
            }

            _plugin->setStateInformation(stateData, stateBytes);

            if (_prepared) {
                juce::String prepareError;

                _plugin->releaseResources();
                if (!configureStereoEffect(*_plugin, _channelCount,
                                           prepareError)) {
                    return juce::Result::fail(prepareError);
                }

                _plugin->setNonRealtime(false);
                _plugin->setRateAndBufferSizeDetails(_sampleRate,
                                                     _maximumBlockSize);
                _plugin->prepareToPlay(_sampleRate, _maximumBlockSize);
                ensureAudioBufferCapacity(_maximumBlockSize, _channelCount);
            }

            _pluginName = _plugin->getName();
            return juce::Result::ok();
        };

        const auto result =
            juce::MessageManager::getInstance()->isThisTheMessageThread()
                ? setStateOnMessageThread()
                : juce::MessageManager::callSync(setStateOnMessageThread)
                      .value_or(juce::Result::fail(
                          "Could not dispatch plug-in state restore to the "
                          "JUCE message thread."));

        if (result.wasOk()) {
            return true;
        }

        error = result.getErrorMessage();
        return false;
    }

    bool showEditor(juce::String& error) {
        auto showEditorOnMessageThread = [&]() -> juce::Result {
            std::scoped_lock lock(_mutex);

            if (_plugin == nullptr) {
                return juce::Result::fail("No plug-in is loaded.");
            }

            if (!_plugin->hasEditor()) {
                return juce::Result::fail(
                    "This plug-in does not provide an editor.");
            }

            if (_editorWindow != nullptr) {
                _editorWindow->reveal();
                return juce::Result::ok();
            }

            auto title =
                _pluginName.isNotEmpty() ? _pluginName : "Plug-in Editor";
            auto window = std::make_unique<PluginEditorWindow>(*_plugin, title);
            if (!window->hasEditorContent()) {
                return juce::Result::fail(
                    "Failed to create the plug-in editor.");
            }

            _editorWindow = std::move(window);
            return juce::Result::ok();
        };

        const auto result =
            juce::MessageManager::getInstance()->isThisTheMessageThread()
                ? showEditorOnMessageThread()
                : juce::MessageManager::callSync(showEditorOnMessageThread)
                      .value_or(juce::Result::fail(
                          "Could not dispatch plug-in editor creation to the "
                          "JUCE message thread."));

        if (result.wasOk()) {
            return true;
        }

        error = result.getErrorMessage();
        return false;
    }

    void process(float* interleavedBuffer, int frames, int channels) {
        if (interleavedBuffer == nullptr || frames <= 0 || channels <= 0) {
            return;
        }

        std::scoped_lock lock(_mutex);

        if (_plugin == nullptr || !_prepared || channels != _channelCount) {
            return;
        }

        ensureAudioBufferCapacity(frames, channels);

        for (int channel = 0; channel < channels; channel++) {
            auto* destination = _audioBuffer.getWritePointer(channel);
            for (int frame = 0; frame < frames; frame++) {
                destination[frame] =
                    interleavedBuffer[(frame * channels) + channel];
            }
        }

        _midiBuffer.clear();
        juce::ScopedNoDenormals noDenormals;
        _plugin->processBlock(_audioBuffer, _midiBuffer);

        for (int channel = 0; channel < channels; channel++) {
            auto* source = _audioBuffer.getReadPointer(channel);
            for (int frame = 0; frame < frames; frame++) {
                interleavedBuffer[(frame * channels) + channel] = source[frame];
            }
        }
    }

  private:
#if JUCE_MAC
    struct RegisteredAudioUnitBundle final {
        RegisteredAudioUnitBundle(juce::String bundlePath,
                                  CFBundleRef bundleRef)
            : path(std::move(bundlePath)), bundle(bundleRef) {}

        RegisteredAudioUnitBundle(RegisteredAudioUnitBundle&& other) noexcept
            : path(std::move(other.path)), bundle(other.bundle) {
            other.bundle = nullptr;
        }

        RegisteredAudioUnitBundle&
        operator=(RegisteredAudioUnitBundle&& other) noexcept {
            if (this == &other) {
                return *this;
            }

            if (bundle != nullptr) {
                CFRelease(bundle);
            }

            path = std::move(other.path);
            bundle = other.bundle;
            other.bundle = nullptr;
            return *this;
        }

        ~RegisteredAudioUnitBundle() {
            if (bundle != nullptr) {
                CFRelease(bundle);
            }
        }

        RegisteredAudioUnitBundle(const RegisteredAudioUnitBundle&) = delete;
        RegisteredAudioUnitBundle&
        operator=(const RegisteredAudioUnitBundle&) = delete;

        juce::String path;
        CFBundleRef bundle = nullptr;
    };
#endif

    bool findPluginDescription(const juce::String& fileOrIdentifier,
                               juce::PluginDescription& result,
                               juce::String& error) {
        if (tryLoadVst3ModuleInfoDescription(fileOrIdentifier, result)) {
            return true;
        }

        if (isAudioUnitBundlePath(fileOrIdentifier)) {
            for (auto* format : _formatManager.getFormats()) {
                if (format->getName() != "AudioUnit" ||
                    !format->fileMightContainThisPluginType(fileOrIdentifier)) {
                    continue;
                }

                auto file =
                    juce::File::createFileWithoutCheckingPath(fileOrIdentifier);
                result.fileOrIdentifier = fileOrIdentifier;
                result.pluginFormatName = format->getName();
                result.name = file.getFileNameWithoutExtension();
                result.descriptiveName = result.name;
                result.lastFileModTime = file.getLastModificationTime();
                result.lastInfoUpdateTime = juce::Time::getCurrentTime();
                return true;
            }
        }

        auto formats = _formatManager.getFormats();

        auto tryScan = [&](bool prefilter) -> bool {
            for (auto* format : formats) {
                if (prefilter &&
                    !format->fileMightContainThisPluginType(fileOrIdentifier)) {
                    continue;
                }

                juce::OwnedArray<juce::PluginDescription> foundTypes;
                format->findAllTypesForFile(foundTypes, fileOrIdentifier);
                if (!foundTypes.isEmpty()) {
                    result = *foundTypes[0];
                    return true;
                }
            }

            return false;
        };

        if (tryScan(true) || tryScan(false)) {
            return true;
        }

        error = "No supported VST3 or Audio Unit plug-in was found in the "
                "selected bundle.";
        return false;
    }

    bool registerAudioUnitBundleIfNeeded(const juce::String& fileOrIdentifier,
                                         juce::String& error) {
#if JUCE_MAC
        if (!isAudioUnitBundlePath(fileOrIdentifier)) {
            return true;
        }

        for (const auto& registeredBundle : _registeredAudioUnitBundles) {
            if (registeredBundle.path == fileOrIdentifier) {
                return true;
            }
        }

        auto utf8Path = fileOrIdentifier.toRawUTF8();
        auto bundleUrl = CFURLCreateFromFileSystemRepresentation(
            kCFAllocatorDefault, reinterpret_cast<const UInt8*>(utf8Path),
            static_cast<CFIndex>(std::strlen(utf8Path)), true);

        if (bundleUrl == nullptr) {
            error = "Failed to resolve the Audio Unit bundle URL.";
            return false;
        }

        auto bundle = CFBundleCreate(kCFAllocatorDefault, bundleUrl);
        CFRelease(bundleUrl);

        if (bundle == nullptr) {
            error = "Failed to open the Audio Unit bundle.";
            return false;
        }

        if (!CFBundleLoadExecutable(bundle)) {
            CFRelease(bundle);
            error = "Failed to load the Audio Unit bundle executable.";
            return false;
        }

        auto componentsValue = CFBundleGetValueForInfoDictionaryKey(
            bundle, CFSTR("AudioComponents"));
        if (componentsValue == nullptr ||
            CFGetTypeID(componentsValue) != CFArrayGetTypeID()) {
            CFRelease(bundle);
            error = "The selected Audio Unit bundle does not declare any "
                    "AudioComponents.";
            return false;
        }

        auto components = static_cast<CFArrayRef>(componentsValue);
        auto componentCount = CFArrayGetCount(components);
        if (componentCount <= 0) {
            CFRelease(bundle);
            error = "The selected Audio Unit bundle does not contain any "
                    "registered components.";
            return false;
        }

        int registeredComponentCount = 0;

        for (CFIndex index = 0; index < componentCount; index++) {
            auto componentValue = CFArrayGetValueAtIndex(components, index);
            if (componentValue == nullptr ||
                CFGetTypeID(componentValue) != CFDictionaryGetTypeID()) {
                continue;
            }

            auto componentDictionary =
                static_cast<CFDictionaryRef>(componentValue);

            OSType type = 0;
            OSType subtype = 0;
            OSType manufacturer = 0;

            if (!tryReadOstype(
                    CFDictionaryGetValue(componentDictionary, CFSTR("type")),
                    type) ||
                !tryReadOstype(
                    CFDictionaryGetValue(componentDictionary, CFSTR("subtype")),
                    subtype) ||
                !tryReadOstype(CFDictionaryGetValue(componentDictionary,
                                                    CFSTR("manufacturer")),
                               manufacturer)) {
                continue;
            }

            auto factoryFunctionName = readStringProperty(
                componentDictionary, CFSTR("factoryFunction"));
            if (factoryFunctionName == nullptr) {
                continue;
            }

            auto factoryPointer =
                CFBundleGetFunctionPointerForName(bundle, factoryFunctionName);
            if (factoryPointer == nullptr) {
                continue;
            }

            AudioComponentDescription description{};
            description.componentType = type;
            description.componentSubType = subtype;
            description.componentManufacturer = manufacturer;

            auto sandboxSafeValue =
                CFDictionaryGetValue(componentDictionary, CFSTR("sandboxSafe"));
            if (sandboxSafeValue != nullptr &&
                CFGetTypeID(sandboxSafeValue) == CFBooleanGetTypeID() &&
                CFBooleanGetValue(
                    static_cast<CFBooleanRef>(sandboxSafeValue))) {
                description.componentFlags |= kAudioComponentFlag_SandboxSafe;
            }

            auto componentName =
                readStringProperty(componentDictionary, CFSTR("name"));
            if (componentName == nullptr) {
                componentName = CFSTR("Audio Unit");
            }

            auto version = readUInt32Property(
                CFDictionaryGetValue(componentDictionary, CFSTR("version")));
            auto component = AudioComponentRegister(
                &description, componentName, version,
                reinterpret_cast<AudioComponentFactoryFunction>(
                    factoryPointer));

            if (component == nullptr) {
                CFRelease(bundle);
                error = "The selected Audio Unit is not currently available in "
                        "macOS's AudioComponent registry, and in-process "
                        "registration failed.";
                return false;
            }

            registeredComponentCount++;
        }

        if (registeredComponentCount == 0) {
            CFRelease(bundle);
            error = "The selected Audio Unit bundle does not expose a usable "
                    "AudioComponent factory.";
            return false;
        }

        _registeredAudioUnitBundles.emplace_back(fileOrIdentifier, bundle);
        return true;
#else
        juce::ignoreUnused(fileOrIdentifier, error);
        return true;
#endif
    }

    juce::AudioPluginFormat*
    findFormatForDescription(const juce::PluginDescription& description) const {
        for (auto* format : _formatManager.getFormats()) {
            if (format->getName() == description.pluginFormatName &&
                format->fileMightContainThisPluginType(
                    description.fileOrIdentifier)) {
                return format;
            }
        }

        return nullptr;
    }

    std::unique_ptr<juce::AudioPluginInstance>
    createPluginInstanceOnMessageThread(
        const juce::PluginDescription& description, double sampleRate,
        int maximumBlockSize, juce::String& error) {
        jassert(juce::MessageManager::getInstance()->isThisTheMessageThread());

        auto* format = findFormatForDescription(description);
        if (format == nullptr) {
            error = "No compatible plug-in format exists for this plug-in.";
            return {};
        }

        if (!format->requiresUnblockedMessageThreadDuringCreation(
                description)) {
            return _formatManager.createPluginInstance(description, sampleRate,
                                                       maximumBlockSize, error);
        }

        juce::WaitableEvent finishedSignal;
        std::unique_ptr<juce::AudioPluginInstance> instance;
        juce::String asyncError;

        _formatManager.createPluginInstanceAsync(
            description, sampleRate, maximumBlockSize,
            [&](std::unique_ptr<juce::AudioPluginInstance> createdInstance,
                const juce::String& callbackError) {
                instance = std::move(createdInstance);
                asyncError = callbackError;
                finishedSignal.signal();
            });

        while (!finishedSignal.wait(10)) {
            pumpMessageThreadWhileWaiting();
        }

        error = asyncError;
        return instance;
    }

    static bool configureStereoEffect(juce::AudioPluginInstance& plugin,
                                      int channels, juce::String& error) {
        const auto requiredLayout = channels == 1
                                        ? juce::AudioChannelSet::mono()
                                        : juce::AudioChannelSet::stereo();

        if (plugin.getBusCount(true) == 0) {
            error = "Instrument plug-ins are not supported yet. Load an effect "
                    "with a stereo input bus.";
            return false;
        }

        if (plugin.getBusCount(false) == 0) {
            error =
                "The selected plug-in does not provide an audio output bus.";
            return false;
        }

        auto layout = plugin.getBusesLayout();
        layout.inputBuses.getReference(0) = requiredLayout;
        layout.outputBuses.getReference(0) = requiredLayout;

        if (!plugin.setBusesLayout(layout)) {
            error = "Only mono or stereo insert effects are supported in this "
                    "build.";
            return false;
        }

        plugin.disableNonMainBuses();

        if (plugin.getMainBusNumInputChannels() != channels ||
            plugin.getMainBusNumOutputChannels() != channels) {
            error = "The selected plug-in could not be configured for a "
                    "matching insert layout.";
            return false;
        }

        return true;
    }

    void ensureAudioBufferCapacity(int frames, int channels) {
        if (_audioBuffer.getNumChannels() != channels ||
            _audioBuffer.getNumSamples() < frames) {
            _audioBuffer.setSize(channels, frames, false, false, true);
        }
    }

    void releaseResourcesLocked() {
        if (_plugin != nullptr && _prepared) {
            _plugin->releaseResources();
        }

        _prepared = false;
    }

    void closeEditorWindowLocked() {
        if (_editorWindow == nullptr) {
            return;
        }

        _editorWindow->dismiss();
        _editorWindow.reset();
    }

    mutable std::mutex _mutex;
    juce::AudioPluginFormatManager _formatManager;
    std::unique_ptr<juce::AudioPluginInstance> _plugin;
    std::unique_ptr<PluginEditorWindow> _editorWindow;
    juce::AudioBuffer<float> _audioBuffer;
    juce::MidiBuffer _midiBuffer;
    juce::String _pluginPath;
    juce::String _pluginName;
#if JUCE_MAC
    std::vector<RegisteredAudioUnitBundle> _registeredAudioUnitBundles;
#endif
    double _sampleRate = 44100.0;
    int _maximumBlockSize = 16384;
    int _channelCount = kDefaultChannelCount;
    bool _prepared = false;
};

#if defined(_WIN32)
#define KINTSUGI_PLUGINHOST_EXPORT extern "C" __declspec(dllexport)
#else
#define KINTSUGI_PLUGINHOST_EXPORT                                             \
    extern "C" __attribute__((visibility("default")))
#endif

KINTSUGI_PLUGINHOST_EXPORT PluginHost* KintsugiPluginHost_Create();
KINTSUGI_PLUGINHOST_EXPORT void KintsugiPluginHost_Destroy(PluginHost* host);
KINTSUGI_PLUGINHOST_EXPORT int KintsugiPluginHost_LoadPlugin(
    PluginHost* host, const char* path, double sampleRate, int maximumBlockSize,
    int channels, char* errorBuffer, int errorBufferBytes);
KINTSUGI_PLUGINHOST_EXPORT int
KintsugiPluginHost_Prepare(PluginHost* host, double sampleRate,
                           int maximumBlockSize, int channels,
                           char* errorBuffer, int errorBufferBytes);
KINTSUGI_PLUGINHOST_EXPORT void
KintsugiPluginHost_UnloadPlugin(PluginHost* host);
KINTSUGI_PLUGINHOST_EXPORT int KintsugiPluginHost_HasPlugin(PluginHost* host);
KINTSUGI_PLUGINHOST_EXPORT int KintsugiPluginHost_HasEditor(PluginHost* host);
KINTSUGI_PLUGINHOST_EXPORT int
KintsugiPluginHost_ShowEditor(PluginHost* host, char* errorBuffer,
                              int errorBufferBytes);
KINTSUGI_PLUGINHOST_EXPORT void
KintsugiPluginHost_ProcessInterleaved(PluginHost* host, void* interleavedBuffer,
                                      int frames, int channels);
KINTSUGI_PLUGINHOST_EXPORT int
KintsugiPluginHost_GetPluginName(PluginHost* host, char* nameBuffer,
                                 int nameBufferBytes);
KINTSUGI_PLUGINHOST_EXPORT int
KintsugiPluginHost_GetStateSize(PluginHost* host);
KINTSUGI_PLUGINHOST_EXPORT int
KintsugiPluginHost_GetState(PluginHost* host, void* stateBuffer,
                            int stateBufferBytes, char* errorBuffer,
                            int errorBufferBytes);
KINTSUGI_PLUGINHOST_EXPORT int
KintsugiPluginHost_SetState(PluginHost* host, const void* stateBuffer,
                            int stateBufferBytes, char* errorBuffer,
                            int errorBufferBytes);

template <typename Function>
int runApiCall(Function&& function, char* errorBuffer,
               int errorBufferBytes) noexcept {
    try {
        const auto result = function();
        if (result) {
            writeStringToBuffer({}, errorBuffer, errorBufferBytes);
            return 1;
        }
    } catch (const std::exception& exception) {
        writeStringToBuffer(toJuceString(exception.what()), errorBuffer,
                            errorBufferBytes);
        return 0;
    } catch (...) {
        writeStringToBuffer("An unknown plug-in host error occurred.",
                            errorBuffer, errorBufferBytes);
        return 0;
    }

    return 0;
}

} // namespace

KINTSUGI_PLUGINHOST_EXPORT PluginHost* KintsugiPluginHost_Create() {
    try {
        getJuceRuntime();
        return new PluginHost();
    } catch (...) {
        return nullptr;
    }
}

KINTSUGI_PLUGINHOST_EXPORT void KintsugiPluginHost_Destroy(PluginHost* host) {
    delete host;
}

KINTSUGI_PLUGINHOST_EXPORT int KintsugiPluginHost_LoadPlugin(
    PluginHost* host, const char* path, double sampleRate, int maximumBlockSize,
    int channels, char* errorBuffer, int errorBufferBytes) {
    return runApiCall(
        [&] {
            if (host == nullptr || path == nullptr) {
                writeStringToBuffer("The plug-in host is not available.",
                                    errorBuffer, errorBufferBytes);
                return false;
            }

            juce::String error;
            const auto loaded = host->loadPlugin(
                path, sampleRate, maximumBlockSize, channels, error);
            if (!loaded) {
                writeStringToBuffer(error, errorBuffer, errorBufferBytes);
            }

            return loaded;
        },
        errorBuffer, errorBufferBytes);
}

KINTSUGI_PLUGINHOST_EXPORT int
KintsugiPluginHost_Prepare(PluginHost* host, double sampleRate,
                           int maximumBlockSize, int channels,
                           char* errorBuffer, int errorBufferBytes) {
    return runApiCall(
        [&] {
            if (host == nullptr) {
                writeStringToBuffer("The plug-in host is not available.",
                                    errorBuffer, errorBufferBytes);
                return false;
            }

            juce::String error;
            const auto prepared =
                host->prepare(sampleRate, maximumBlockSize, channels, error);
            if (!prepared) {
                writeStringToBuffer(error, errorBuffer, errorBufferBytes);
            }

            return prepared;
        },
        errorBuffer, errorBufferBytes);
}

KINTSUGI_PLUGINHOST_EXPORT void
KintsugiPluginHost_UnloadPlugin(PluginHost* host) {
    if (host != nullptr) {
        host->unload();
    }
}

KINTSUGI_PLUGINHOST_EXPORT int KintsugiPluginHost_HasPlugin(PluginHost* host) {
    return host != nullptr && host->hasPlugin() ? 1 : 0;
}

KINTSUGI_PLUGINHOST_EXPORT int KintsugiPluginHost_HasEditor(PluginHost* host) {
    return host != nullptr && host->hasEditor() ? 1 : 0;
}

KINTSUGI_PLUGINHOST_EXPORT int
KintsugiPluginHost_ShowEditor(PluginHost* host, char* errorBuffer,
                              int errorBufferBytes) {
    return runApiCall(
        [&] {
            if (host == nullptr) {
                writeStringToBuffer("The plug-in host is not available.",
                                    errorBuffer, errorBufferBytes);
                return false;
            }

            juce::String error;
            const auto shown = host->showEditor(error);
            if (!shown) {
                writeStringToBuffer(error, errorBuffer, errorBufferBytes);
            }

            return shown;
        },
        errorBuffer, errorBufferBytes);
}

KINTSUGI_PLUGINHOST_EXPORT void
KintsugiPluginHost_ProcessInterleaved(PluginHost* host, void* interleavedBuffer,
                                      int frames, int channels) {
    if (host == nullptr || interleavedBuffer == nullptr) {
        return;
    }

    try {
        host->process(static_cast<float*>(interleavedBuffer), frames, channels);
    } catch (...) {
    }
}

KINTSUGI_PLUGINHOST_EXPORT int
KintsugiPluginHost_GetPluginName(PluginHost* host, char* nameBuffer,
                                 int nameBufferBytes) {
    if (host == nullptr) {
        writeStringToBuffer({}, nameBuffer, nameBufferBytes);
        return 0;
    }

    writeStringToBuffer(host->getPluginName(), nameBuffer, nameBufferBytes);
    return 1;
}

KINTSUGI_PLUGINHOST_EXPORT int
KintsugiPluginHost_GetStateSize(PluginHost* host) {
    if (host == nullptr || !host->hasPlugin()) {
        return 0;
    }

    juce::String error;
    auto state = host->getState(error);
    juce::ignoreUnused(error);
    return static_cast<int>(state.size());
}

KINTSUGI_PLUGINHOST_EXPORT int
KintsugiPluginHost_GetState(PluginHost* host, void* stateBuffer,
                            int stateBufferBytes, char* errorBuffer,
                            int errorBufferBytes) {
    return runApiCall(
        [&] {
            if (host == nullptr) {
                writeStringToBuffer("The plug-in host is not available.",
                                    errorBuffer, errorBufferBytes);
                return false;
            }

            juce::String error;
            auto state = host->getState(error);
            if (error.isNotEmpty()) {
                writeStringToBuffer(error, errorBuffer, errorBufferBytes);
                return false;
            }

            if (static_cast<int>(state.size()) > stateBufferBytes) {
                writeStringToBuffer(
                    "The destination plug-in state buffer is too small.",
                    errorBuffer, errorBufferBytes);
                return false;
            }

            if (!state.empty() && stateBuffer != nullptr) {
                std::memcpy(stateBuffer, state.data(), state.size());
            }

            return true;
        },
        errorBuffer, errorBufferBytes);
}

KINTSUGI_PLUGINHOST_EXPORT int
KintsugiPluginHost_SetState(PluginHost* host, const void* stateBuffer,
                            int stateBufferBytes, char* errorBuffer,
                            int errorBufferBytes) {
    return runApiCall(
        [&] {
            if (host == nullptr) {
                writeStringToBuffer("The plug-in host is not available.",
                                    errorBuffer, errorBufferBytes);
                return false;
            }

            juce::String error;
            const auto restored =
                host->setState(stateBuffer, stateBufferBytes, error);
            if (!restored) {
                writeStringToBuffer(error, errorBuffer, errorBufferBytes);
            }

            return restored;
        },
        errorBuffer, errorBufferBytes);
}
