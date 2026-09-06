import useBaseUrl from '@docusaurus/useBaseUrl';

type Props = {
  src: string;
  alt: string;
  width: number;
  height: number;
  sizes: string;
  className?: string;
  eager?: boolean;
  priority?: boolean;
};

/** Real product artwork with smaller delivery sizes and an unchanged original. */
export default function ProductScreenshot({src, alt, width, height, sizes, className, eager = false, priority = false}: Props) {
  const url = useBaseUrl(src);
  const variants = [480, 960, 1440, 1920].filter((size) => size < width);
  const srcSet = [...variants.map((size) => `${url.replace(/\.webp$/, `-${size}.webp`)} ${size}w`), `${url} ${width}w`].join(', ');
  return <img src={url} srcSet={srcSet} sizes={sizes} alt={alt} width={width} height={height} className={className} loading={eager ? 'eager' : 'lazy'} decoding="async" fetchPriority={priority ? 'high' : 'auto'} />;
}
