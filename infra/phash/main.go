// prismedia-phash — video pHash helper matching Stash's sprite pHash pipeline 1:1.
//
// The hash format and pipeline MUST stay byte-compatible with Stash
// (pkg/hash/videophash/phash.go) because Prismedia shares StashDB's fingerprint
// index. Any divergence produces hashes that do not cluster with existing
// community fingerprints.
//
// Pipeline:
//  1. For i in [0, 25): time = 0.05*duration + i * (0.9*duration/25)
//  2. ffmpeg -ss <time> -i <file> -frames:v 1 -vf scale=160:-2 -c:v bmp -f rawvideo -
//     (seek BEFORE -i — input seek — is load-bearing)
//  3. Decode BMP, paste each frame into a 5x5 NRGBA montage via disintegration/imaging
//  4. goimagehash.PerceptionHash(montage) → uint64
//  5. Print as lowercase 16-char hex
//
// Usage: prismedia-phash -file <path> -duration <seconds>
package main

import (
	"bytes"
	"flag"
	"fmt"
	"image"
	"image/color"
	"math"
	"os"
	"os/exec"
	"strconv"

	"github.com/corona10/goimagehash"
	"github.com/disintegration/imaging"

	_ "golang.org/x/image/bmp"
)

const (
	screenshotWidth = 160
	columns         = 5
	rows            = 5
	chunkCount      = columns * rows
)

func main() {
	var filePath string
	var duration float64
	flag.StringVar(&filePath, "file", "", "video file path")
	flag.Float64Var(&duration, "duration", 0, "video duration in seconds")
	flag.Parse()

	if filePath == "" || duration <= 0 {
		fmt.Fprintln(os.Stderr, "usage: prismedia-phash -file <path> -duration <seconds>")
		os.Exit(2)
	}

	if _, err := os.Stat(filePath); err != nil {
		fmt.Fprintf(os.Stderr, "stat input: %v\n", err)
		os.Exit(1)
	}

	frames, err := extractFrames(filePath, duration)
	if err != nil {
		fmt.Fprintf(os.Stderr, "extract frames: %v\n", err)
		os.Exit(1)
	}

	montage := combineImages(frames)

	hash, err := goimagehash.PerceptionHash(montage)
	if err != nil {
		fmt.Fprintf(os.Stderr, "perception hash: %v\n", err)
		os.Exit(1)
	}

	fmt.Printf("%016x\n", hash.GetHash())
}

func extractFrames(filePath string, duration float64) ([]image.Image, error) {
	offset := 0.05 * duration
	stepSize := (0.9 * duration) / float64(chunkCount)

	frames := make([]image.Image, chunkCount)

	// Offsets (in seconds) to retry at when the primary seek returns
	// garbage BMP output. This happens on files with occasional corrupt
	// frames or when the seek lands on a non-decodable packet. We keep
	// the retry window small so the overall hash stays representative of
	// the original time points.
	retryDeltas := []float64{0, -0.25, 0.25, -1.0, 1.0}

	var lastGood image.Image
	var lastErr error
	for i := 0; i < chunkCount; i++ {
		base := offset + float64(i)*stepSize
		var img image.Image
		var err error
		for _, delta := range retryDeltas {
			t := base + delta
			if t < 0 {
				continue
			}
			if t >= duration {
				continue
			}
			img, err = seekFrame(filePath, t)
			if err == nil {
				break
			}
		}
		if err != nil {
			lastErr = fmt.Errorf("frame %d at t=%.3f: %w", i, base, err)
			// Fall back to the previous good frame so the montage stays
			// well-formed. If no good frame exists yet we leave a nil slot
			// and patch it on a second pass.
			if lastGood != nil {
				frames[i] = lastGood
			}
			continue
		}
		frames[i] = img
		lastGood = img
	}

	// Backfill any leading nil frames with the first successful frame.
	var first image.Image
	for _, f := range frames {
		if f != nil {
			first = f
			break
		}
	}
	if first == nil {
		// Every frame failed — the file is almost certainly corrupt.
		if lastErr != nil {
			return nil, lastErr
		}
		return nil, fmt.Errorf("no frames decoded")
	}
	for i, f := range frames {
		if f == nil {
			frames[i] = first
		}
	}
	return frames, nil
}

// seekFrame invokes ffmpeg with the same argument order Stash emits via
// transcoder.ScreenshotTime + ScreenshotOutputTypeBMP.
func seekFrame(filePath string, t float64) (image.Image, error) {
	cmd := exec.Command(
		"ffmpeg",
		"-loglevel", "error",
		"-y",
		"-ss", strconv.FormatFloat(t, 'f', -1, 64),
		"-i", filePath,
		"-frames:v", "1",
		"-vf", fmt.Sprintf("scale=%d:-2", screenshotWidth),
		"-c:v", "bmp",
		"-f", "rawvideo",
		"-",
	)
	var stdout, stderr bytes.Buffer
	cmd.Stdout = &stdout
	cmd.Stderr = &stderr
	if err := cmd.Run(); err != nil {
		return nil, fmt.Errorf("ffmpeg: %v (%s)", err, stderr.String())
	}
	img, _, err := image.Decode(&stdout)
	if err != nil {
		return nil, fmt.Errorf("decode bmp: %w", err)
	}
	return img, nil
}

// combineImages mirrors Stash pkg/hash/videophash/phash.go combineImages:
// canvas dimensions come from the first frame (not a fixed 800x800), NRGBA
// background, disintegration/imaging.Paste for placement.
func combineImages(images []image.Image) image.Image {
	if len(images) == 0 {
		return imaging.New(1, 1, color.NRGBA{})
	}
	width := images[0].Bounds().Size().X
	height := images[0].Bounds().Size().Y
	canvasWidth := width * columns
	canvasHeight := height * rows
	montage := imaging.New(canvasWidth, canvasHeight, color.NRGBA{})
	for index := 0; index < len(images); index++ {
		x := width * (index % columns)
		y := height * int(math.Floor(float64(index)/float64(rows)))
		montage = imaging.Paste(montage, images[index], image.Pt(x, y))
	}
	return montage
}
