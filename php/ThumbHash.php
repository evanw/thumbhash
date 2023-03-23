<?php

class ThumbHash
{
    public static function thumbHashToApproximateAspectRatio($hash) {
      $header = $hash[3];
      $hasAlpha = $hash[2] & 0x80;
      $isLandscape = $hash[4] & 0x80;
      $lx = $isLandscape ? $hasAlpha ? 5 : 7 : $header & 7;
      $ly = $isLandscape ? $header & 7 : $hasAlpha ? 5 : 7;

      return $lx / $ly;
    }

    public static function thumbHashToAverageRGBA($hash) {
      $header = $hash[0] | ($hash[1] << 8) | ($hash[2] << 16);
      $l = ($header & 63) / 63;
      $p = (($header >> 6) & 63) / 31.5 - 1;
      $q = (($header >> 12) & 63) / 31.5 - 1;
      $hasAlpha = $header >> 23;
      $a = $hasAlpha ? ($hash[5] & 15) / 15 : 1;
      $b = $l - 2 / 3 * $p;
      $r = (3 * $l - $b + $q) / 2;
      $g = $r - $q;

      return [
        "r" => max(0, min(1, $r)),
        "g" => max(0, min(1, $g)),
        "b" => max(0, min(1, $b)),
        "a" => $a
      ];
    }

    public static function encodeHash($hash) {
      return rtrim(base64_encode(implode(array_map("chr", $hash))), '=');
    }

    public static function decodeHash($hash) {
      $result = array_map("ord", str_split(base64_decode($hash . '=')));

      return $result;
    }

    public static function rgbaToThumbHash($w, $h, $rgba) {
      // Encoding an image larger than 100x100 is slow with no benefit
      if ($w > 100 || $h > 100) throw new Exception("$w x $h doesn't fit in 100 x 100");
      
      // Determine the average color
      $avg_r = 0; $avg_g = 0; $avg_b = 0; $avg_a = 0;
      for ($i = 0, $j = 0; $i < $w * $h; $i++, $j += 4) {
        $alpha = $rgba[$j + 3] / 255;
        $avg_r += $alpha / 255 * $rgba[$j];
        $avg_g += $alpha / 255 * $rgba[$j + 1];
        $avg_b += $alpha / 255 * $rgba[$j + 2];
        $avg_a += $alpha;
      }
      if ($avg_a) {
        $avg_r /= $avg_a;
        $avg_g /= $avg_a;
        $avg_b /= $avg_a;
      }

      $hasAlpha = $avg_a < $w * $h;
      $l_limit = $hasAlpha ? 5 : 7; // Use fewer luminance bits if there's alpha
      $lx = max(1, round($l_limit * $w / max($w, $h)));
      $ly = max(1, round($l_limit * $h / max($w, $h)));
      $l = []; // luminance
      $p = []; // yellow - blue
      $q = []; // red - green
      $a = []; // alpha

      // Convert the image from RGBA to LPQA (composite atop the average color)
      for ($i = 0, $j = 0; $i < $w * $h; $i++, $j += 4) {
        $alpha = $rgba[$j + 3] / 255;
        $r = $avg_r * (1 - $alpha) + $alpha / 255 * $rgba[$j];
        $g = $avg_g * (1 - $alpha) + $alpha / 255 * $rgba[$j + 1];
        $b = $avg_b * (1 - $alpha) + $alpha / 255 * $rgba[$j + 2];
        $l[$i] = ($r + $g + $b) / 3;
        $p[$i] = ($r + $g) / 2 - $b;
        $q[$i] = $r - $g;
        $a[$i] = $alpha;
      }

      $encodeChannel = function($channel, $nx, $ny) use ($w, $h) {
        $dc = 0;
        $ac = [];
        $scale = 0;
        $fx = [];
      
        for ($cy = 0; $cy < $ny; $cy++) {
          for ($cx = 0; $cx * $ny < $nx * ($ny - $cy); $cx++) {
            $f = 0;
            for ($x = 0; $x < $w; $x++) {
              $fx[$x] = cos(pi() / $w * $cx * ($x + 0.5));
            }
            for ($y = 0; $y < $h; $y++) {
              for ($x = 0, $fy = cos(pi() / $h * $cy * ($y + 0.5)); $x < $w; $x++) {
                $f += $channel[$x + $y * $w] * $fx[$x] * $fy;
              }
            }
            $f /= $w * $h;
      
            if ($cx || $cy) {
              array_push($ac, $f);
              $scale = max($scale, abs($f));
            } else {
              $dc = $f;
            }
          }
        }
      
        if ($scale) {
          for ($i = 0; $i < count($ac); $i++) {
            $ac[$i] = 0.5 + 0.5 / $scale * $ac[$i];
          }
        }
      
        return [ $dc, $ac, $scale ];
      };

      [$l_dc, $l_ac, $l_scale] = $encodeChannel($l, max(3, $lx), max(3, $ly), $w, $h);
      [$p_dc, $p_ac, $p_scale] = $encodeChannel($p, 3, 3, $w, $h);
      [$q_dc, $q_ac, $q_scale] = $encodeChannel($q, 3, 3, $w, $h);
      [$a_dc, $a_ac, $a_scale] = $hasAlpha ? $encodeChannel($a, 5, 5, $w, $h) : [0, 0, 0];

      // Write the constants
      $isLandscape = $w > $h;
      $header24 = round(63 * $l_dc) | (round(31.5 + 31.5 * $p_dc) << 6) | (round(31.5 + 31.5 * $q_dc) << 12) | (round(31 * $l_scale) << 18) | ($hasAlpha << 23);
      $header16 = ($isLandscape ? $ly : $lx) | (round(63 * $p_scale) << 3) | (round(63 * $q_scale) << 9) | ($isLandscape << 15);
      $hash = [ $header24 & 255, ($header24 >> 8) & 255, $header24 >> 16, $header16 & 255, $header16 >> 8 ];
      $isOdd = false;

      if ($hasAlpha) {
        array_push($hash, round(15 * $a_dc) | (round(15 * $a_scale) << 4));
      }

      foreach ($hasAlpha ? [$l_ac, $p_ac, $q_ac, $a_ac] : [$l_ac, $p_ac, $q_ac] as $ac) {
        foreach ($ac as $f) {
          $f15 = round(15 * $f);
          $i15 = (int) $f15;

          if ($isOdd) {
            $hash[count($hash) - 1] |= $i15 << 4;
          } else {
            array_push($hash, $i15);
          }
          $isOdd = !$isOdd;
          }
      }

      $uint8Array = array_values(unpack('C*', call_user_func_array('pack', array_merge(array('C*'), $hash))));

      return $uint8Array;
    }

    public function thumbHashToRGBA($hash) {
      // Read the constants
      $header24 = $hash[0] | ($hash[1] << 8) | ($hash[2] << 16);
      $header16 = $hash[3] | ($hash[4] << 8);
      $l_dc = ($header24 & 63) / 63;
      $p_dc = (($header24 >> 6) & 63) / 31.5 - 1;
      $q_dc = (($header24 >> 12) & 63) / 31.5 - 1;
      $l_scale = (($header24 >> 18) & 31) / 31;
      $hasAlpha = $header24 >> 23;
      $p_scale = (($header16 >> 3) & 63) / 63;
      $q_scale = (($header16 >> 9) & 63) / 63;
      $isLandscape = $header16 >> 15;
      $lx = max(3, $isLandscape ? $hasAlpha ? 5 : 7 : $header16 & 7);
      $ly = max(3, $isLandscape ? $header16 & 7 : $hasAlpha ? 5 : 7);
      $a_dc = $hasAlpha ? ($hash[5] & 15) / 15 : 1;
      $a_scale = ($hash[5] >> 4) / 15;
    
      // Read the varying factors (boost saturation by 1.25x to compensate for quantization)
      $ac_start = $hasAlpha ? 6 : 5;
      $ac_index = 0;

      $decodeChannel = function($nx, $ny, $scale) use ($hash, &$ac_start, &$ac_index) {
        $ac = [];

        for ($cy = 0; $cy < $ny; $cy++) {
          for ($cx = $cy ? 0 : 1; $cx * $ny < $nx * ($ny - $cy); $cx++) {
            $val = ((($hash[$ac_start + ($ac_index >> 1)] >> (($ac_index++ & 1) << 2)) & 15) / 7.5 - 1) * $scale;
            array_push($ac, $val);
          }
        }
        return $ac;
      };

      $l_ac = $decodeChannel($lx, $ly, $l_scale, $hash);
      $p_ac = $decodeChannel(3, 3, $p_scale * 1.25, $hash);
      $q_ac = $decodeChannel(3, 3, $q_scale * 1.25, $hash);
      $a_ac = null;

      if ($hasAlpha) {
        $a_ac = $decodeChannel(5, 5, $a_scale, $hash);
      }
    
      // Decode using the DCT into RGB
      $ratio = ThumbHash::thumbHashToApproximateAspectRatio($hash);
      $w = round($ratio > 1 ? 32 : 32 * $ratio);
      $h = round($ratio > 1 ? 32 / $ratio : 32);
      $rgba = new \SplFixedArray($w * $h * 4);
      $fx = [];
      $fy = [];

      for ($y = 0, $i = 0; $y < $h; $y++) {
        for ($x = 0; $x < $w; $x++, $i += 4) {
          $l = $l_dc; 
          $p = $p_dc; 
          $q = $q_dc; 
          $a = $a_dc;
      
          // Precompute the coefficients
          for ($cx = 0, $n = max($lx, $hasAlpha ? 5 : 3); $cx < $n; $cx++) {
            $fx[$cx] = cos(pi() / $w * ($x + 0.5) * $cx);
          }
          for ($cy = 0, $n = max($ly, $hasAlpha ? 5 : 3); $cy < $n; $cy++) {
            $fy[$cy] = cos(pi() / $h * ($y + 0.5) * $cy);
          }
      
          // Decode L
          for ($cy = 0, $j = 0; $cy < $ly; $cy++) {
            for ($cx = $cy ? 0 : 1, $fy2 = $fy[$cy] * 2; $cx * $ly < $lx * ($ly - $cy); $cx++, $j++) {
              $l += $l_ac[$j] * $fx[$cx] * $fy2;
            }
          }
      
          // Decode P and Q
          for ($cy = 0, $j = 0; $cy < 3; $cy++) {
            for ($cx = $cy ? 0 : 1, $fy2 = $fy[$cy] * 2; $cx < 3 - $cy; $cx++, $j++) {
              $f = $fx[$cx] * $fy2;
              $p += $p_ac[$j] * $f;
              $q += $q_ac[$j] * $f;
            }
          }
    
          // Decode A
          if ($hasAlpha) {
            for ($cy = 0, $j = 0; $cy < 5; $cy++) {
              for ($cx = $cy ? 0 : 1, $fy2 = $fy[$cy] * 2; $cx < 5 - $cy; $cx++, $j++) {
                $a += $a_ac[$j] * $fx[$cx] * $fy2;
              }
            }
          }
    
          // Convert to RGB
          $b = $l - 2 / 3 * $p;
          $r = (3 * $l - $b + $q) / 2;
          $g = $r - $q;
          $rgba[$i] = (int) max(0, 255 * min(1, $r));
          $rgba[$i + 1] = (int) max(0, 255 * min(1, $g));
          $rgba[$i + 2] = (int) max(0, 255 * min(1, $b));
          $rgba[$i + 3] = (int) max(0, 255 * min(1, $a));
        }
      }

      return [
        "width" => (int) $w,
        "height" => (int) $h,
        "rgba" => $rgba->toArray(),
      ];
    }

    public function rgbaToDataURL($w, $h, $rgba) {

      function unsigned_shift_right($v, $n) {
        return ($v & 0xFFFFFFFF) >> ($n & 0x1F);
      }

      $row = $w * 4 + 1;
      $idat = 6 + $h * (5 + $row);

      $bytes = [
        137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0,
        $w >> 8, $w & 255, 0, 0, $h >> 8, $h & 255, 8, 6, 0, 0, 0, 0, 0, 0, 0,
        unsigned_shift_right($idat, 24), ($idat >> 16) & 255, ($idat >> 8) & 255, $idat & 255,
        73, 68, 65, 84, 120, 1
      ];

      $table = [
        0, 498536548, 997073096, 651767980, 1994146192, 1802195444, 1303535960,
        1342533948, -306674912, -267414716, -690576408, -882789492, -1687895376,
        -2032938284, -1609899400, -1111625188
      ];

      $a = 1;
      $b = 0;
        
      for ($y = 0, $i = 0, $end = $row - 1; $y < $h; $y++, $end += $row - 1) {
        $bytes[] = $y + 1 < $h ? 0 : 1;
        $bytes[] = $row & 255;
        $bytes[] = $row >> 8;
        $bytes[] = ~$row & 255;
        $bytes[] = ($row >> 8) ^ 255;
        $bytes[] = 0;

        for ($b = ($b + $a) % 65521; $i < $end; $i++) {
          $u = $rgba[$i] & 255;
          $bytes[] = $u;

          $a = ($a + $u) % 65521;
          $b = ($b + $a) % 65521;
        }
      }

      $bytes[] = $b >> 8;
      $bytes[] = $b & 255;
      $bytes[] = $a >> 8;
      $bytes[] = $a & 255;
      $bytes[] = 0;
      $bytes[] = 0;
      $bytes[] = 0;
      $bytes[] = 0;
      $bytes[] = 0;
      $bytes[] = 0;
      $bytes[] = 0;
      $bytes[] = 0;
      $bytes[] = 73;
      $bytes[] = 69;
      $bytes[] = 78;
      $bytes[] = 68;
      $bytes[] = 174;
      $bytes[] = 66;
      $bytes[] = 96;
      $bytes[] = 130;

      // crc
      foreach ([[12, 29], [37, 41 + $idat]] as list($start, $end)) {
        $c = ~0;

        for ($i = $start; $i < $end; $i++) {
          $c ^= $bytes[$i];
          $c = unsigned_shift_right($c, 4) ^ $table[$c & 15];
          $c = unsigned_shift_right($c, 4) ^ $table[$c & 15];
        }

        $c = ~$c;
        $bytes[$end++] = unsigned_shift_right($c, 24);
        $bytes[$end++] = ($c >> 16) & 255;
        $bytes[$end++] = ($c >> 8) & 255;
        $bytes[$end++] = $c & 255;
      }

      return 'data:image/png;base64,' . base64_encode(implode(array_map("chr", $bytes)));
    }
}
