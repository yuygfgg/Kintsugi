#import <Foundation/Foundation.h>
#import <MediaPlayer/MediaPlayer.h>

typedef void (*MediaCommandCallback)(int command); // 0 = play, 1 = pause, 2 = stop, 3 = toggle
typedef void (*MediaSeekCallback)(double position);

static MediaCommandCallback _callback = NULL;
static MediaSeekCallback _seekCallback = NULL;

@interface MediaControlManager : NSObject
@end

@implementation MediaControlManager

+ (instancetype)shared {
    static MediaControlManager *instance = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        instance = [[MediaControlManager alloc] init];
    });
    return instance;
}

- (instancetype)init {
    if (self = [super init]) {
        [self setupRemoteCommandCenter];
    }
    return self;
}

- (void)setupRemoteCommandCenter {
    MPRemoteCommandCenter *commandCenter = [MPRemoteCommandCenter sharedCommandCenter];
    
    [commandCenter.playCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent * _Nonnull event) {
        if (_callback) _callback(0);
        return MPRemoteCommandHandlerStatusSuccess;
    }];
    
    [commandCenter.pauseCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent * _Nonnull event) {
        if (_callback) _callback(1);
        return MPRemoteCommandHandlerStatusSuccess;
    }];
    
    [commandCenter.stopCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent * _Nonnull event) {
        if (_callback) _callback(2);
        return MPRemoteCommandHandlerStatusSuccess;
    }];
    
    [commandCenter.togglePlayPauseCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent * _Nonnull event) {
        if (_callback) _callback(3);
        return MPRemoteCommandHandlerStatusSuccess;
    }];
    
    [commandCenter.nextTrackCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent * _Nonnull event) {
        if (_callback) _callback(4);
        return MPRemoteCommandHandlerStatusSuccess;
    }];
    
    [commandCenter.previousTrackCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent * _Nonnull event) {
        if (_callback) _callback(5);
        return MPRemoteCommandHandlerStatusSuccess;
    }];
    
    if (@available(macOS 10.12.2, *)) {
        [commandCenter.changePlaybackPositionCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent * _Nonnull event) {
            MPChangePlaybackPositionCommandEvent *positionEvent = (MPChangePlaybackPositionCommandEvent *)event;
            if (_seekCallback) _seekCallback(positionEvent.positionTime);
            return MPRemoteCommandHandlerStatusSuccess;
        }];
    }
}

- (void)updateNowPlaying:(NSString *)title artist:(NSString *)artist duration:(double)duration position:(double)position {
    NSMutableDictionary *nowPlayingInfo = [NSMutableDictionary dictionary];
    if (title) nowPlayingInfo[MPMediaItemPropertyTitle] = title;
    if (artist) nowPlayingInfo[MPMediaItemPropertyArtist] = artist;
    
    if (duration > 0) {
        nowPlayingInfo[MPMediaItemPropertyPlaybackDuration] = @(duration);
    }
    nowPlayingInfo[MPNowPlayingInfoPropertyElapsedPlaybackTime] = @(position);
    
    double rate = 0.0;
    if (@available(macOS 10.12.2, *)) {
        if ([MPNowPlayingInfoCenter defaultCenter].playbackState == MPNowPlayingPlaybackStatePlaying) {
            rate = 1.0;
        }
    }
    nowPlayingInfo[MPNowPlayingInfoPropertyPlaybackRate] = @(rate);
    
    [MPNowPlayingInfoCenter defaultCenter].nowPlayingInfo = nowPlayingInfo;
}

- (void)updatePlaybackState:(int)state position:(double)position {
    NSMutableDictionary *nowPlayingInfo = [NSMutableDictionary dictionaryWithDictionary:[MPNowPlayingInfoCenter defaultCenter].nowPlayingInfo];
    nowPlayingInfo[MPNowPlayingInfoPropertyElapsedPlaybackTime] = @(position);
    nowPlayingInfo[MPNowPlayingInfoPropertyPlaybackRate] = @(state == 1 ? 1.0 : 0.0);
    [MPNowPlayingInfoCenter defaultCenter].nowPlayingInfo = nowPlayingInfo;

    if (@available(macOS 10.12.2, *)) {
        if (state == 1) {
            [MPNowPlayingInfoCenter defaultCenter].playbackState = MPNowPlayingPlaybackStatePlaying;
        } else if (state == 2) {
            [MPNowPlayingInfoCenter defaultCenter].playbackState = MPNowPlayingPlaybackStatePaused;
        } else {
            [MPNowPlayingInfoCenter defaultCenter].playbackState = MPNowPlayingPlaybackStateStopped;
        }
    }
}

- (void)updatePosition:(double)position {
    NSMutableDictionary *nowPlayingInfo = [NSMutableDictionary dictionaryWithDictionary:[MPNowPlayingInfoCenter defaultCenter].nowPlayingInfo];
    nowPlayingInfo[MPNowPlayingInfoPropertyElapsedPlaybackTime] = @(position);
    [MPNowPlayingInfoCenter defaultCenter].nowPlayingInfo = nowPlayingInfo;
}

@end

__attribute__((visibility("default")))
void InitMediaControls(MediaCommandCallback callback, MediaSeekCallback seekCallback) {
    _callback = callback;
    _seekCallback = seekCallback;
    [MediaControlManager shared];
}

__attribute__((visibility("default")))
void UpdateNowPlaying(const char* title, const char* artist, double duration, double position) {
    NSString *nsTitle = title ? [NSString stringWithUTF8String:title] : nil;
    NSString *nsArtist = artist ? [NSString stringWithUTF8String:artist] : nil;
    [[MediaControlManager shared] updateNowPlaying:nsTitle artist:nsArtist duration:duration position:position];
}

__attribute__((visibility("default")))
void UpdatePlaybackState(int state, double position) {
    [[MediaControlManager shared] updatePlaybackState:state position:position];
}

__attribute__((visibility("default")))
void UpdatePlaybackPosition(double position) {
    [[MediaControlManager shared] updatePosition:position];
}
