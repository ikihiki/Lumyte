import '@testing-library/jest-dom/vitest';
import {vi} from 'vitest';
vi.stubGlobal('NodeFilter',{SHOW_ELEMENT:1,FILTER_ACCEPT:1,FILTER_REJECT:2,FILTER_SKIP:3});
class TestMutationObserver { observe(){} disconnect(){} takeRecords(){return [];} }
vi.stubGlobal('MutationObserver',TestMutationObserver);
class TestResizeObserver { observe(){} unobserve(){} disconnect(){} }
vi.stubGlobal('ResizeObserver',TestResizeObserver);
Object.defineProperty(HTMLElement.prototype,'focus',{configurable:true,writable:true,value:function(){}});
