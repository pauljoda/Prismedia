import MDXComponents from '@theme-original/MDXComponents';
import DocScreenshot, {DocScreenshotGrid} from '@site/src/components/docs/DocScreenshot';
import DocTable from '@site/src/components/docs/DocTable';

export default {...MDXComponents, table: DocTable, DocScreenshot, DocScreenshotGrid};
